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
| `MainWindow.xaml` / `.cs` | 메인 화면과 동작 |
| `SplashWindow.xaml` / `.cs` | 시작 스플래시 화면 |
| `App.xaml` / `.cs` | 앱 진입점 (스플래시 → 메인) |
| `CodecWpf.csproj` | 프로젝트 설정 |
| `bg.png`, `codec.ico` | 배경·아이콘 리소스 (exe에 내장) |

## 참고

- 이미지 리소스는 XAML에서 `pack://application:,,,/파일명` 형식으로 참조합니다.
  단일 실행 파일에서 경로가 깨지지 않게 하기 위한 표준 방식입니다.

## 라이선스

MIT License
