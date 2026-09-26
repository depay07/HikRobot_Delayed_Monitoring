# SC6000 Delayed Monitor — FTP 수신 이미지 표시

PC의 FTP 수신 폴더에 저장되는 이미지를 저장 시간순으로 읽고, 지정한 개수만큼 늦춰 표시합니다.
저장 이미지에 들어 있는 그래픽과 OK/NG를 그대로 보여줍니다. PC에서 판정을 다시 만들지 않습니다.
카메라 SDK 접속, Procedure 이름, 이미지/판정 출력 이름은 설정하지 않습니다.

## 설정

실행파일 옆의 config.ini에서 다음을 설정하세요.

```ini
CAMERAS=1
MONITOR=0
INSPECTION_TEXT=링 유무검사
DELAY_COUNT=14
IMAGE_FOLDER=D:\vision

[CAMERA1]
TITLE=1번 카메라
```

- IMAGE_FOLDER는 PC의 FTP 수신 최상위 폴더입니다.
- D:\vision\2026\09\26 같은 모든 하위 날짜 폴더를 자동 탐색합니다. 날짜 변경 시 경로 수정이나 재시작이 필요 없습니다.
- DELAY_COUNT=14이면 새 이미지 15개째 저장 시 1번 이미지를, 16개째 저장 시 2번 이미지를 표시합니다.
- 0이면 새 이미지를 바로 표시합니다. 잘못된 지연 값은 0으로 처리하고 로그를 남깁니다.
- 프로그램 시작 전에 있던 과거 이미지는 제외합니다. 새로 저장되는 이미지부터 순번 1로 시작하며, 재시작하면 지연 기록도 새로 시작합니다.
- 이미지 순서는 파일의 수정한 시간(LastWriteTimeUtc) 기준입니다. 같은 시간이면 전체 파일 경로순으로 처리합니다.
- 저장 중인 파일은 완료 후 읽습니다. 폴더 확인 주기는 200ms이며, 지연 횟수는 시간이 아닌 읽은 이미지 수로만 증가합니다.
- PNG, JPG/JPEG, BMP, GIF, TIF/TIFF를 읽습니다. 사진과 그래픽을 포함한 저장 이미지 전체를 원래 비율로 화면에 맞춥니다.
- 파일은 읽기만 하며 이동·삭제·변경하지 않습니다.
- 설정 수정 후 프로그램을 다시 실행하세요. 설명은 모두 한글로 제공됩니다.

## 여러 카메라

CAMERAS=1은 전체 화면, 2는 좌우, 3~4는 2행 2열입니다. 각 카메라에 독립적인 폴더와 지연 큐를 사용합니다.
예를 들어 카메라 2대는 다음처럼 각 CAMERA 구역에 IMAGE_FOLDER를 지정합니다.

```ini
CAMERAS=2
DELAY_COUNT=14
[CAMERA1]
TITLE=1번 카메라
IMAGE_FOLDER=D:\vision\camera1
[CAMERA2]
TITLE=2번 카메라
IMAGE_FOLDER=D:\vision\camera2
```

각 카메라 폴더 안의 날짜 폴더는 자동으로 읽습니다.
logo.*는 실행 폴더에 넣으면 다음 실행에 적용되며 INSPECTION_TEXT는 로고 오른쪽에 표시됩니다.

## 빌드 및 검증

SC6000DelayedMonitor.sln을 Visual Studio 또는 MSBuild로 빌드합니다.
.NET Framework 4.6.1을 사용합니다. 기존 VisionMaster 설치 DLL 참조는 기반 프로젝트에 유지되어 있지만
FTP 이미지 표시 경로에서는 SDK를 호출하지 않습니다.
실행파일: SC6000DelayedMonitor/bin/Release/SC6000DelayedMonitor.exe
config.ini가 없으면 빌드 시 config.example.ini로 초기 설정을 생성합니다.

```powershell
.\tests\run-tests.ps1
.\tests\run-tests.ps1 -Layout
```

자동 검증: 지연 0/1/14, 15번째 이미지에서 1번째 표시, 저장 시간순 정렬, 같은 파일 중복 집계 방지,
날짜 폴더 변경, 늦게 생성되는 폴더, FTP 기록 중 파일 재시도, 이미지 메모리 해제,
1~4대의 서로 다른 프로세스 화면 배치(800×600~3840×2160).
FTP 서버 대신 실제 폴더에 이미지 파일을 기록하여 테스트하며 카메라는 제어하지 않습니다.

이미지 큐는 최대 DELAY_COUNT장, UI 갱신 대기는 1장으로 제한합니다. 화면이 바쁘면 갱신은 최신 결과로 합치지만,
이미지 집계는 생략하지 않습니다. 실제 수신 이미지·현장 config.ini·로그·bin/obj·테스트 산출물은 Git에서 제외합니다.
기존 HikRobot_Monitoring 원본과 실행파일은 수정하지 않습니다.