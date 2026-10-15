# CODEC (Desktop)

사이버펑크 테마의 실시간 인코더 / 디코더 데스크톱 앱입니다.
텍스트를 입력하면 즉시 변환되며, 시작 시 네온 스플래시 화면이 재생됩니다. .NET WPF로 제작되었습니다.

```
</>  CODEC
realtime encoder / decoder
```

## 기능

- **실시간 변환** — 타이핑하는 즉시 결과가 갱신됩니다.
- **9가지 방식** — base64, base32, hex, url, binary, ascii, rot13, caesar, morse
- **인코딩 / 디코딩 토글** — 상단 토글로 방향을 전환합니다.
- **스왑** — 가운데 방향 라벨을 클릭하면 출력을 입력으로 보내고 모드를 뒤집습니다.
- **AGAIN (Ctrl+Enter)** — 출력을 입력으로 보내고 모드는 유지한 채 한 번 더 변환합니다. 여러 겹 인코딩된 문자열을 한 겹씩 벗길 때 사용하며, 중간에 방식을 바꿔 `url → base64` 같은 조합도 처리할 수 있습니다.
- **auto (자동 다중 디코딩)** — 디코딩 결과가 또 인코딩된 형태면 계속 풀어냅니다(최대 10겹). 거친 단계는 상태바에 `base64 → url (2 layers)`처럼 표시됩니다. url, binary, ascii, hex, base32, base64, morse를 판별하며, rot13/caesar는 결과만으로 판별할 수 없어 제외됩니다.
- **html** — HTML 엔티티 인코딩/디코딩 (`<` ↔ `&lt;`, `&#54620;` 같은 숫자 엔티티 포함). auto 판별 대상에도 포함됩니다.
- **hex 출력 형식** — hex 인코딩 시 `4865`, `48 65`, `%48%65`, `\x48\x65`, `0x48 0x65` 중 선택. 디코딩은 이 형식들과 `48:65`, `48-65`를 모두 자동으로 받아들입니다.
- **해시** — md5, sha1, sha256, sha512 (텍스트는 선택한 문자셋 기준, 인코딩 전용).
- **파일 해시** — 입력창에 파일을 끌어다 놓으면 MD5/SHA1/SHA256/SHA512를 한 번에 계산합니다. 여러 파일도 가능하며, 큰 파일도 화면이 멈추지 않습니다.
- **문자셋 (UTF-8 / EUC-KR)** — 오른쪽 아래 버튼으로 전환. base64·base32·hex·url·binary·ascii·해시·auto에 적용되며, EUC-KR은 CP949로 처리해 `똠`·`햏` 같은 확장 글자도 지원합니다. UTF-8로 디코딩했는데 EUC-KR 한글로 보이면 전환하라고 안내하고, auto는 `%C7%D1` 같은 EUC-KR URL을 UTF-8 모드에서도 자동으로 풀어냅니다.
- **TOOLS 창** — 헤더의 TOOLS 버튼으로 여는 별도 창.
  - **진법** — 2/8/10/16진수 중 아무 칸에나 입력하면 나머지가 즉시 바뀜 (큰 수, 음수, 0x·0b 접두사 지원)
  - **IP** — IPv4 ↔ 10진수 ↔ 16진수 자동 판별 변환, 2진수 표기와 사설망/루프백 등 종류 표시
  - **타임스탬프** — Unix 타임스탬프(초/ms/µs 자동 판별) ↔ 날짜, UTC·로컬·ISO 8601 표시
  - **파일 ↔ Base64** — 파일을 끌어다 놓으면 Base64로, Base64(data URI 포함)를 붙여넣으면 파일로 복원 (형식 자동 추측)
- **찾기 / 바꾸기 (Ctrl+F / Ctrl+H)** — 입력창 위에 검색 막대 표시. Enter 다음, Shift+Enter 이전, Esc 닫기, 대소문자 구분 토글, 모두 바꾸기. 마지막으로 클릭한 창(입력/출력)에서 찾고, 바꾸기는 입력창에서만 됩니다.
- **줄 · 글자 수** — 각 창 오른쪽 아래에 커서 위치(Ln/Col), 줄 수, 글자 수, 선택한 문자셋 기준 바이트 수 표시.
- **복사** — 출력 결과를 클립보드로 복사합니다.
- **사이버펑크 UI** — 네온 글로우, 입체 패널, 그리드 배경, 스플래시 애니메이션.
- 한글(UTF-8) 텍스트도 처리할 수 있습니다.

## 요구 사항

- Windows (x64)
- .NET 10 SDK (빌드 시). 배포된 단일 실행 파일은 .NET 설치 없이 실행됩니다.

## 개발 실행

프로젝트 폴더에서:

```powershell
dotnet run
```

## 단일 실행 파일(exe) 빌드

.NET이 설치되지 않은 PC나 USB에서도 실행되는 독립 실행 파일을 만듭니다.

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

빌드 결과는 다음 위치에 생성됩니다:

```
bin\Release\net10.0-windows\win-x64\publish\codec.exe
```

이 `codec.exe` 하나만 복사하면 다른 PC에서도 실행됩니다.
(`IncludeNativeLibrariesForSelfExtract` 옵션은 USB·타 PC에서 네이티브 DLL 로드 오류를 방지하기 위해 필요합니다.)

## 프로젝트 구성

| 파일 | 역할 |
|------|------|
| `Codec.cs` | 인코딩 / 디코딩 로직 (UI와 분리) |
| `Tools.cs` | 도구 창 계산 로직 (진법·IP·타임스탬프·Base64 파일) |
| `ToolsWindow.xaml` / `.cs` | TOOLS 창 화면과 동작 |
| `MainWindow.xaml` / `.cs` | 메인 화면과 동작 |
| `SplashWindow.xaml` / `.cs` | 시작 스플래시 화면 |
| `App.xaml` / `.cs` | 앱 진입점 (스플래시 → 메인), 공용 색상·스타일 |
| `CodecWpf.csproj` | 프로젝트 설정 |
| `bg.png`, `codec.ico` | 배경·아이콘 리소스 (exe에 내장) |

## 참고

- 이미지 리소스는 XAML에서 `pack://application:,,,/파일명` 형식으로 참조합니다.
  단일 실행 파일에서 경로가 깨지지 않게 하기 위한 표준 방식입니다.

## 라이선스

MIT License
