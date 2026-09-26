# SC6000 Delayed Monitor

SC6000 내부에서 실행되는 검사를 PC가 Remote SDK로 모니터링합니다.
실행파일은 **SC6000DelayedMonitor.exe**입니다. 기존 SC6000DualMonitor와 소스, 설정,
bin/obj, Git 저장소를 공유하지 않습니다.

## 빌드와 설정

1. VisionMaster 4.4.x SDK와 Visual Studio의 .NET Framework 4.6.1 개발 도구를 설치합니다.
2. SC6000DelayedMonitor.sln을 열어 Release로 빌드합니다.
3. SC6000DelayedMonitor/bin/Release/config.ini에 현장 설정을 입력합니다.
   프로젝트 config.ini가 없으면 빌드 시 config.example.ini를 복사합니다.
   프로젝트 config.ini를 수정한 후 다시 빌드해도 됩니다.
4. 같은 폴더의 SC6000DelayedMonitor.exe를 실행합니다.

SDK 기본 위치는 `C:\Program Files\VisionMaster4.4.50\Development\V4.x\ComControls\Assembly`입니다.
다른 경로는 MSBuild의 `/p:VisionMasterAssemblyDir="실제 Assembly 폴더"`로 지정합니다.
SDK DLL은 저장소에 포함하지 않습니다. 실제 SDK 설치가 필요합니다.

```ini
CAMERAS=1
MONITOR=0
INSPECTION_TEXT=
DELAY_COUNT=14

[CAMERA1]
IP=192.0.2.10
PASSWORD=
TITLE=CAMERA 1
PROCEDURE=
IMAGE_OUTPUT=
RESULT_OUTPUT=
OK_VALUE=1
NG_VALUE=0
```

예제 IP는 통신용 실제 주소가 아닙니다. IP와 비밀번호를 현장 값으로 바꾸세요.
Procedure·출력명은 현재 미정이므로 비워 두었습니다. 다음을 Solution 설계에 맞춰 설정해야 합니다.

- PROCEDURE: 제품 1개당 정확히 1회 완료되는 흐름 이름.
- IMAGE_OUTPUT: 그 흐름의 **이미지형 출력** 이름.
- RESULT_OUTPUT: 같은 흐름의 **정수형 출력** 이름. 검사당 정수 1개만 허용합니다.
- OK_VALUE, NG_VALUE: 실제 판정값. 예제의 1/0을 현장 값으로 확인·수정하세요.
  SDK 실행 성공 여부를 제품 OK/NG로 대신 사용하지 않습니다.

출력 설정이 비어 있으면 Operation Interface는 연결하고 지연 화면에 설정 필요 안내를 표시합니다.
설정은 시작 시 읽으므로 config.ini를 바꾼 후 프로그램을 다시 실행하세요.
‘결과 다시 연결’은 현재 설정으로 콜백을 다시 구독하고 지연 기록을 초기화합니다.
카메라의 검사를 시작·정지하거나 Solution을 전환하지 않습니다.

## 표시와 지연 규칙

- 카메라 1대는 전체 영역, 2대는 좌우, 3~4대는 2×2입니다. 카메라마다 별도 Viewer 프로세스입니다.
- 각 Viewer 왼쪽은 실시간 Operation Interface, 오른쪽은 Delayed Monitor입니다.
- DELAY_COUNT=14: 검사 1~14 동안 대기, 검사 15 완료 때 검사 1을 표시합니다.
- DELAY_COUNT=0은 현재 검사입니다. 누락·음수·비정수·int 범위 초과는 0으로 처리하고 로그에 기록합니다.
- 지연은 결과 콜백을 받은 횟수로 계산합니다. 시간 지연이나 Sleep을 사용하지 않습니다.
  기존 200ms UI 타이머는 창 크기 맞춤과 자식 프로세스 상태 확인에만 사용합니다.
- UI가 바쁘면 대기 화면 갱신은 최신 것으로 합칩니다. 큐의 검사 횟수는 생략하지 않으며,
  표시 가능한 최신 검사에서 DELAY_COUNT를 뺀 결과를 보여줍니다. 모든 중간 화면을 재생하는 녹화 기능은 아닙니다.
- 큐 최대 DELAY_COUNT장, UI 갱신 대기 최대 1장, 현재 표시 최대 1장입니다.
  콜백 변환 중 임시 이미지가 추가로 필요합니다. 메모리는 해상도·카메라 수에 비례합니다.
- logo.*를 실행 폴더에 넣으면 다음 실행부터 적용됩니다. INSPECTION_TEXT는 로고 오른쪽에 표시됩니다.

## 오류와 연결 변경

출력 누락, 이미지 변환 실패, 예상과 다른 판정값, Procedure 실행 오류는 지연 표시를 중단하고
이미지 큐를 비웁니다. 실패 검사를 건너뛰어 제품과 결과의 순서를 어긋나게 하지 않습니다.
VmException에는 16진수 errorCode를 표시합니다. 로그는 실행 폴더의 logs에 프로세스별로 저장합니다.

SDK Solution 로딩 시작/완료, 서버 상태·Proxy 종료, Procedure 해제, 인스턴스 변경 이벤트에서
지연 기록과 표시 이미지를 제거합니다. Solution 로딩 완료 후 새 Procedure를 다시 찾습니다.
SDK가 유효한 ExecuteCount를 주면 중복을 제외하고 번호 불연속 시 기록을 초기화합니다.
연결 장애 후 자동 재접속은 구현하지 않았으며 프로그램을 다시 실행하면 빈 큐에서 시작합니다.
외부 Solution 변경에서 SDK 알림이 실제 도착하는지는 SC6000 현장에서 확인해야 합니다.

## 확인 범위

설치 DLL과 공식 샘플을 대조해 컴파일했습니다. API 근거는 SDK_NOTES.md에 있습니다.
현장 Procedure/출력명이 미정이므로 실제 카메라 이미지·판정 수신 검증은 아직 완료되지 않았습니다.
배포 전 제품 1개당 콜백 1회, 이미지·판정의 동일 검사 대응, 케이블 분리/재연결,
외부 Solution 변경/재로딩 알림과 첫 14회 대기를 확인하세요.

```powershell
.\tests\run-tests.ps1
.\tests\run-tests.ps1 -Layout
```

SDK 설치와 Windows 데스크톱 세션이 필요합니다. 테스트는 카메라에 접속하거나 검사를 실행하지 않습니다.
-Layout은 실제 SDK 컨트롤을 가진 자식 프로세스로 1~4대, 5가지 창 크기를 확인합니다.
테스트 대기는 창 생성·이벤트 처리용이며 검사 지연 알고리즘에는 포함되지 않습니다.
실제 config.ini, 현장 로고, 로그, SDK DLL, bin/obj, test-results는 Git에서 제외합니다.
