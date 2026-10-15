# 제3자 구성요소 고지 (THIRD-PARTY NOTICES)

몽PDF는 아래 제3자 구성요소를 사용하거나 함께 배포한다. 각 구성요소는 해당 저작권자의 라이선스를 따르며, 그 조건은 몽PDF의 GPL-3.0 라이선스(`LICENSE`)와 별개로 유지된다.

## 구성요소 목록

| 구성요소 | 버전 | 라이선스 | 출처 | 고지 파일 위치 |
|---|---|---|---|---|
| PDFium (`pdfium.dll`, x64) | 157.0.8086.0 | BSD-3-Clause AND Apache-2.0 | https://pdfium.googlesource.com/pdfium/ | `third_party/pdfium/licenses/pdfium.txt` |
| pdfium-binaries (PDFium 빌드 배포본) | 157.0.8086.0 | MIT | https://github.com/bblanchon/pdfium-binaries | `third_party/pdfium/LICENSE` |
| qpdf (`qpdf30.dll`, x64) | 확인 필요(DLL에 버전 정보 없음) | Apache-2.0 (v7 이전 Artistic-2.0 선택 가능) | https://github.com/qpdf/qpdf | `third_party/qpdf/LICENSE.txt`, `third_party/qpdf/NOTICE.md` |
| Microsoft Visual C++ 런타임 (`msvcp140.dll`, `vcruntime140.dll`, `vcruntime140_1.dll`, x64) | 14.51.36247.0 (msvcp140.dll 기준) | Microsoft 소프트웨어 사용 조건(재배포 가능 파일) | https://learn.microsoft.com/cpp/windows/redistributing-visual-cpp-files | 아래 설명 참고 |

## PDFium

- 용도: PDF 렌더링 (`pdfium.dll`, x64)
- 저작권: Copyright 2014 The PDFium Authors (Google)
- 라이선스: BSD-3-Clause 및 Apache License 2.0 (전문: `third_party/pdfium/licenses/pdfium.txt`)
- 원본: https://pdfium.googlesource.com/pdfium/
- 배포본 출처: 본 프로그램은 Benoit Blanchon의 pdfium-binaries(https://github.com/bblanchon/pdfium-binaries, Copyright 2014-2025 Benoit Blanchon, MIT License)가 빌드한 미리 컴파일된 바이너리를 **수정 없이** 포함한다. 전문: `third_party/pdfium/LICENSE`
- 수정 여부: 수정하지 않았다.

### PDFium에 정적 링크된 하위 구성요소

라이선스 전문은 모두 `third_party/pdfium/licenses/`에 있다.

| 구성요소 | 고지 파일 |
|---|---|
| Abseil | `abseil.txt` |
| AGG | `agg23.txt` |
| dragonbox | `dragonbox-Apache2-LLVM.txt`, `dragonbox-Boost.txt` |
| fast_float | `fast_float.txt` |
| FreeType | `freetype.txt` (FreeType License: 문서에 사용 사실 고지 필요) |
| HarfBuzz | `harfbuzz.txt` |
| ICU | `icu.txt` (Unicode License) |
| Little CMS | `lcms.txt` |
| libjpeg-turbo | `libjpeg_turbo.ijg`, `libjpeg_turbo.md` |
| OpenJPEG | `libopenjpeg.txt` |
| libpng | `libpng.txt` |
| LLVM libc | `llvm-libc.txt` |
| simdutf | `simdutf.txt` |
| zlib | `zlib.txt` |

이 소프트웨어는 FreeType 프로젝트(https://freetype.org)의 작업물을 일부 포함한다. 이 소프트웨어는 독립 JPEG 그룹(Independent JPEG Group)의 작업을 일부 기반으로 한다.

## qpdf

- 용도: PDF 병합·분할·최적화 등 구조 처리 (`qpdf30.dll`, x64)
- 저작권: Copyright (c) 2005-2021 Jay Berkenbilt, 2022-2026 Jay Berkenbilt and Manfred Holger
- 라이선스: Apache License 2.0 (qpdf 7 이전 버전의 Artistic License 2.0을 선택할 수도 있다). 전문과 하위 코드(Rijndael 공개 도메인 구현, sphlib sha2, qtest) 고지는 `third_party/qpdf/LICENSE.txt`, `third_party/qpdf/NOTICE.md`에 있다.
- 출처: https://github.com/qpdf/qpdf (공식 배포 바이너리 사용. 정확한 버전은 공개 전 확인 필요)
- 수정 여부: 수정하지 않았다.

## Microsoft Visual C++ 런타임

- 용도: qpdf DLL이 의존하는 런타임 (`third_party/qpdf/x64/`의 `msvcp140.dll`, `vcruntime140.dll`, `vcruntime140_1.dll`)
- 라이선스: Microsoft Visual Studio의 재배포 가능 파일(redistributable)로서 Microsoft 소프트웨어 사용 조건에 따라 앱과 함께 재배포한다. 이 파일들은 GPL-3.0이 아니며 Microsoft의 소유다. 재배포 조건은 https://learn.microsoft.com/cpp/windows/redistributing-visual-cpp-files 에 있다.
- 수정 여부: 수정하지 않았다.

## 빌드 도구 (배포하지 않음)

- ConfuserEx(https://github.com/mkaring/ConfuserEx, v1.6.0)는 `tools/get_tools.ps1`로 내려받아 빌드 때만 쓰며 배포본에 포함하지 않는다.

## 라이선스 원문 보관 위치

각 구성요소의 라이선스 원문(저작권 표시 포함)은 위 표의 고지 파일 위치에 받은 그대로 보관한다. 설치본에도 `third_party/pdfium/LICENSE`, `third_party/pdfium/licenses/`, `third_party/qpdf/LICENSE.txt`, `third_party/qpdf/NOTICE.md`를 포함해 사용자가 열람할 수 있게 한다.
