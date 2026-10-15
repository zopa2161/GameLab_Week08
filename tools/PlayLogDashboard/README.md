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

`runtime/`은 로컬 실행용으로 유지하며 `.gitignore`로 Git에서 제외합니다. 나머지 소스·실행 파일 9개를 관리합니다. Git으로 새로 받은 사람은 별도로 전달받은 `runtime` 폴더를 같은 위치에 넣어야 합니다. 전체 폴더를 직접 복사해 전달할 때는 `runtime`도 포함하세요.

이미 스테이징한 runtime 파일은 SourceTree에서 스테이징을 해제해야 제외 설정이 적용됩니다. 이미 커밋된 파일에는 제외 설정만으로 추적 해제가 적용되지 않습니다. Git 상태와 커밋 여부는 확인하지 않았습니다.

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
