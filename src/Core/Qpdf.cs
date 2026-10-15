using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PdfCore
{
    // qpdf(Apache 2.0)로 암호 걸기·해제와 무손실 최적화를 한다. PDFium에는 암호를 거는 기능이 없다.
    internal static class Qpdf
    {
        private const string Dll = "qpdf30.dll";
        private const CallingConvention Cc = CallingConvention.Cdecl;

        private const int QPDF_ERRORS = 2;
        private const int ObjectStreamGenerate = 2;
        private const int DecodeGeneralized = 1;
        private const int PrintFull = 0;
        private const int PrintNone = 2;

        [DllImport(Dll, CallingConvention = Cc)] private static extern IntPtr qpdf_init();
        [DllImport(Dll, CallingConvention = Cc)] private static extern void qpdf_cleanup(ref IntPtr qpdf);
        [DllImport(Dll, CallingConvention = Cc)] private static extern void qpdf_silence_errors(IntPtr qpdf);
        [DllImport(Dll, CallingConvention = Cc)] private static extern void qpdf_set_suppress_warnings(IntPtr qpdf, int value);
        [DllImport(Dll, CallingConvention = Cc)]
        private static extern int qpdf_read_memory(IntPtr qpdf, byte[] description, IntPtr buffer, ulong size, byte[] password);
        [DllImport(Dll, CallingConvention = Cc)] private static extern void qpdf_set_object_stream_mode(IntPtr qpdf, int mode);
        [DllImport(Dll, CallingConvention = Cc)] private static extern void qpdf_set_compress_streams(IntPtr qpdf, int value);
        [DllImport(Dll, CallingConvention = Cc)] private static extern void qpdf_set_decode_level(IntPtr qpdf, int level);
        [DllImport(Dll, CallingConvention = Cc)] private static extern void qpdf_set_preserve_encryption(IntPtr qpdf, int value);
        [DllImport(Dll, CallingConvention = Cc)]
        private static extern void qpdf_set_r6_encryption_parameters2(IntPtr qpdf, byte[] userPassword, byte[] ownerPassword,
            int allowAccessibility, int allowExtract, int allowAssemble, int allowAnnotateAndForm, int allowFormFilling,
            int allowModifyOther, int print, int encryptMetadata);
        [DllImport(Dll, CallingConvention = Cc)] private static extern int qpdf_init_write_memory(IntPtr qpdf);
        [DllImport(Dll, CallingConvention = Cc)] private static extern int qpdf_write(IntPtr qpdf);
        [DllImport(Dll, CallingConvention = Cc)] private static extern UIntPtr qpdf_get_buffer_length(IntPtr qpdf);
        [DllImport(Dll, CallingConvention = Cc)] private static extern IntPtr qpdf_get_buffer(IntPtr qpdf);
        [DllImport(Dll, CallingConvention = Cc)] private static extern IntPtr qpdf_get_error(IntPtr qpdf);
        [DllImport(Dll, CallingConvention = Cc)] private static extern IntPtr qpdf_get_error_full_text(IntPtr qpdf, IntPtr error);

        // security가 null이면 원래 암호 상태를 유지하고(password로 열어 다시 씀), 아니면 그 설정으로 바꾼다.
        public static byte[] Rewrite(byte[] input, string password, PdfSecurity security, bool optimize)
        {
            IntPtr q = qpdf_init();
            GCHandle pin = GCHandle.Alloc(input, GCHandleType.Pinned);
            try
            {
                qpdf_silence_errors(q);
                qpdf_set_suppress_warnings(q, 1);
                int rc = qpdf_read_memory(q, Z("document"), pin.AddrOfPinnedObject(), (ulong)input.Length,
                    password == null ? null : Z(password));
                Check(q, rc, "문서를 다시 읽지 못했습니다.");
                // 쓰기 설정은 반드시 qpdf_init_write_memory 다음에 해야 한다 (그 전에는 쓰기 객체가 없어 충돌한다).
                Check(q, qpdf_init_write_memory(q), "저장을 준비하지 못했습니다.");

                if (security == null)
                {
                    qpdf_set_preserve_encryption(q, 1);
                }
                else if (security.IsNone)
                {
                    qpdf_set_preserve_encryption(q, 0);
                }
                else
                {
                    bool restricted = !(security.AllowPrint && security.AllowCopy && security.AllowEdit && security.AllowAnnotate);
                    string user = security.UserPassword ?? "";
                    string owner = security.OwnerPassword;
                    // 권한 암호를 따로 주지 않았으면, 제한이 있을 때는 아무도 모르는 임의 암호를, 없을 때는 열기 암호를 쓴다.
                    if (string.IsNullOrEmpty(owner)) owner = restricted ? Guid.NewGuid().ToString("N") : user;
                    qpdf_set_r6_encryption_parameters2(q, Z(user), Z(owner),
                        1, B(security.AllowCopy), B(security.AllowEdit), B(security.AllowAnnotate), B(security.AllowAnnotate),
                        B(security.AllowEdit), security.AllowPrint ? PrintFull : PrintNone, 1);
                }

                if (optimize)
                {
                    qpdf_set_object_stream_mode(q, ObjectStreamGenerate);
                    qpdf_set_compress_streams(q, 1);
                    qpdf_set_decode_level(q, DecodeGeneralized);
                }

                Check(q, qpdf_write(q), "저장하지 못했습니다.");
                long len = (long)qpdf_get_buffer_length(q).ToUInt64();
                var output = new byte[len];
                Marshal.Copy(qpdf_get_buffer(q), output, 0, (int)len);
                return output;
            }
            finally
            {
                qpdf_cleanup(ref q);
                pin.Free();
            }
        }

        private static void Check(IntPtr q, int rc, string message)
        {
            if ((rc & QPDF_ERRORS) == 0) return;
            string detail = "";
            IntPtr err = qpdf_get_error(q);
            if (err != IntPtr.Zero)
            {
                IntPtr text = qpdf_get_error_full_text(q, err);
                if (text != IntPtr.Zero) detail = " (" + Utf8(text) + ")";
            }
            throw new PdfException(PdfOpenError.Unknown, message + detail);
        }

        private static string Utf8(IntPtr p)
        {
            int n = 0;
            while (Marshal.ReadByte(p, n) != 0) n++;
            var buf = new byte[n];
            Marshal.Copy(p, buf, 0, n);
            return Encoding.UTF8.GetString(buf);
        }

        private static int B(bool v)
        {
            return v ? 1 : 0;
        }

        private static byte[] Z(string s)
        {
            return Encoding.UTF8.GetBytes(s + "\0");
        }
    }
}
