# 플레이 로그 분석 배포판

## 다른 사람에게 전달하기

이 `PlayLogDashboard` 폴더 전체를 전달하세요. 받는 사람은 **PlayLogDashboard.exe**를 더블클릭하면 됩니다. Python·Codex·Unity 설치나 패키지 다운로드는 필요하지 않습니다.

- Windows 10/11 64비트용입니다. Windows에 포함된 .NET Framework 4.5 이상을 사용합니다.
- `PlayLogDashboard.exe`, `app`, `runtime`을 함께 유지하세요. exe만 복사하면 실행되지 않습니다.
- 브라우저가 자동으로 열립니다. 열리지 않으면 실행 창의 '브라우저 열기'를 누르세요.
- 실행 창의 '종료'를 누르거나 창을 닫으면 해당 서버도 종료됩니다.
- 실행 창을 찾을 수 없으면 `Stop.cmd`로 이 폴더에서 실행한 프로그램을 종료할 수 있습니다.
- 포트는 실행할 때 비어 있는 로컬 포트로 선택합니다. 다른 프로그램과 같은 8501 포트를 쓸 필요가 없습니다.
- JSONL 파일을 여러 개 넣거나 로그 폴더를 지정하세요. '유의미하게 보는 법' 탭에서 해석 안내와 보고서를 확인할 수 있습니다.
- 개인 로그는 배포판에 넣지 않았습니다. 분석할 로그를 직접 선택하세요.

## Unity와 Git

위치는 프로젝트 루트의 `tools/PlayLogDashboard`입니다. `Assets` 밖이므로 Unity Project 창과 게임 빌드에는 포함되지 않습니다.

`runtime/`은 로컬 실행·배포에만 사용하며 `.gitignore`로 Git에서 제외합니다. 소스와 실행 파일 등 약 9개 파일만 저장소에서 관리합니다. 이 폴더 전체를 다른 사람에게 직접 전달할 때는 `runtime/`까지 함께 복사하세요.

Git으로 새로 받은 작업 폴더에는 실행 환경이 없습니다. 처음에는 별도로 전달받은 `runtime` 폴더를 같은 위치에 넣어야 exe를 실행할 수 있습니다. 이미 실행 환경이 있는 PC에서는 Python 스크립트 수정 후 재실행하면 됩니다.

`runtime/`을 이미 스테이징했다면 SourceTree에서 해당 폴더의 스테이징을 해제하세요. 이미 커밋했다면 `.gitignore`만으로 추적이 해제되지 않습니다. 저장소 변경 상태는 Git 화면에서 확인하세요.

실행 로그는 `%LOCALAPPDATA%/PlayLogDashboard` 아래에만 저장됩니다. 분석한 로그 원문을 이 폴더에 저장하지 않습니다.

## 구성

- `PlayLogDashboard.exe`: 실행 창과 서버 수명 관리
- `Launcher.cs`: 실행 파일 소스
- `build_runtime.py`: 설치된 실행 환경을 상대 경로 배포판으로 복사하는 빌드 도구
- `app/app.py`, `app/analysis.py`: 대시보드와 집계 소스
- `app/requirements.txt`: 포함된 패키지 버전
- `runtime`: 독립 Python 실행 환경과 패키지, 각 패키지의 라이선스
- `runtime/LICENSE.txt`: Python 라이선스

실행 환경은 이 PC에서 검증한 Python 3.12와 패키지를 복사해 구성했습니다. 사용자 PC의 Python이나 Codex 경로를 참조하지 않습니다. 다른 OS용 실행 파일은 포함하지 않습니다.

## 실행 파일 다시 만들기

소스를 변경한 경우 Windows의 C# 컴파일러로 빌드합니다.

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /out:PlayLogDashboard.exe Launcher.cs
```

Python 코드만 수정했다면 실행 창을 닫고 다시 열면 됩니다.

## 확인한 동작

- 포함된 Python만으로 Streamlit·pandas·Plotly·PyArrow를 불러오고 서버를 시작했습니다.
- 독립 실행 파일의 브라우저 화면과 실제 로그 폴더 입력을 확인했습니다.
- 한글과 공백이 있는 다른 폴더로 전체 복사한 뒤 실행 환경 불러오기와 서버 준비 상태를 확인했습니다. 외부 Python 설치 경로는 사용하지 않았습니다.
- `--stop` 종료 신호로 실행 창과 하위 서버가 함께 종료되는 것을 확인했습니다. `Stop.cmd`도 같은 기능을 호출합니다.
- 별도 Windows PC에서의 실행은 아직 확인하지 못했습니다. 브라우저 파일 선택을 통한 다중 업로드와 다운로드 저장은 받는 PC에서 확인이 필요합니다.
- 배포 폴더 용량은 약 291MiB이며, 실제 개인 로그와 예제 파일은 포함하지 않습니다.

Git CLI·커밋·push는 실행하지 않았으며 Git 변경 상태는 확인하지 않았습니다.
