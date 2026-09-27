# 제품별 Queue 및 썸네일 변경 기록

## 수정 전 분석과 복구 지점

현재 작업 폴더: C:\Users\zenyl\source\repos\HikRobot_Delayed_Monitoring

기존 D: 작업 폴더는 없어졌으며, 위 Visual Studio 복제본과 Documents의 사본 및 GitHub main은 모두
1a98f51 소스였습니다. 소스 변경 전 복구 지점으로 **1785476** 커밋을 만들었습니다.
기존 소스가 이미 커밋되어 있어서 이 체크포인트는 소스 변경 없는 커밋입니다.
현장 config.ini와 이미지 파일은 저장소에 포함하지 않았습니다.

| 기능 | 기존 위치 및 동작 |
|---|---|
| 설정 | Program.cs / IniConfig.Load, GetCamera. DELAY_COUNT, IP/PORT/PASSWORD, Solution 자동 로딩, 모니터/항상 위 설정 |
| 실행 | Program.Main → MonitorForm → 카메라별 ViewerForm 자식 프로세스 |
| 카메라 | CameraStartup.Start, SdkCameraConnection.Connect/Load. 실제 VM Remote SDK 접속 후 선택 경로 자동 로딩 |
| 이미지 수신 | ViewerForm.Scan → FolderImageSource.Scan. 200ms 폴더 탐색, 날짜 하위 폴더, 저장 시간순 처리 |
| 중복/저장 완료 | FolderImageSource의 경로 집합, 크기·수정시각 안정 확인, 파일 독점 읽기. 이 경로를 유지 |
| 재시작 | FolderImageSource 생성 시 이미 있던 파일을 제외. 복구 재생 로직 없음 |
| 지연 | InspectionBuffer.Push. OK/NG와 무관하게 모든 이미지 Enqueue 후 Count > Delay일 때 Dequeue |
| 표시 | ViewerForm의 UI 타이머 → FolderImageSource.Take → DelayedPane.ShowResult / PictureBox.Zoom |
| UI | MonitorForm의 1~4대 분할, MonitorHeader의 ES PACK/검사명, CameraLayout의 자식 창 크기 맞춤 |
| NG 카운터 | 기존 소스에 NG 전용 카운터나 파일명 판정 로직 없음 |
| 부저/작업자 Reset | 기존 소스에 없음. 사용자 확인 결과 물리적 스위치이며 이번 작업에서 제외 |
| 기존 Reset 메서드 | InspectionBuffer.Reset은 내부 버퍼 정리/Dispose 용도이며 작업자 Reset 입력과 연결되어 있지 않음 |

기존 설정은 DELAY_COUNT=14였습니다. 이번 요청에 따라 새 설정을 13으로 변경했습니다.
계산 방식 자체는 기존 방식 그대로입니다. 입력 #1 이후 13개가 추가로 들어온 **입력 #14**에서 #1이 도착합니다.
부저의 물리적 타이밍을 측정하거나 변경한 것은 아닙니다.

## 변경 파일

- InspectionBuffer.cs: 파일명에서 _NG_/_OK_와 마지막 숫자를 읽어 Item에 저장. 선행 0을 보존.
  알 수 없는 파일명은 판정 미확인으로 표시하고 이동 횟수에는 포함. 기존 FIFO를 유지하고 최대 Delay+1개 썸네일 이력을 추가.
- FolderImageSource.cs: 원래 수신/중복 방지/파일 읽기 흐름 유지. UI용 독립 썸네일 스냅샷과 지연 이미지를 같은 잠금 안에서 전달.
  이미지 처리 실패 시 미소유 Bitmap 해제. 정상 입력이 큐에 들어간 뒤 순번 증가.
- InspectionTrailControl.cs: 새 오른쪽 표시 컨트롤. 2열, Delay=13일 때 7행. 이동 0부터 13까지 표시.
  NG 빨간색 테두리/글자, 도착 위치 금색 테두리. 이미지 비율 유지.
- DelayedPane.cs: 기존 주 이미지 옆에 썸네일 영역을 배치. 기존 헤더/회사명/카메라 분할은 유지.
- ViewerForm.cs: 이미지와 썸네일 스냅샷을 UI 스레드에서 갱신. 버전이 바뀐 경우만 썸네일 복사.
- SC6000DelayedMonitor.csproj: 새 컨트롤 소스 등록. 기존 SDK 참조와 빌드 설정 유지.
- config.example.ini 및 로컬 config.ini: DELAY_COUNT=13과 해당 설명. 다른 현장 설정값 유지.
- tests/QueueTests.cs: 제품별 이동·연속 NG·파일명 파싱·메모리·썸네일 표시 검증 추가.
- tests/BufferTests.cs, LayoutTests.cs, run-tests.ps1: 새 테스트 연결, 13회 썸네일 배치 확인, 테스트 빌드 시 실행 폴더 설정 보존.

## 연속 NG와 물리적 Reset

#100 NG와 #101 NG는 서로 다른 Item입니다. #100이 도착했을 때 #101은 이동 12/13 위치에 있고,
다음 이미지 입력에서 #101이 도착합니다. 이전 표시 이미지를 교체하거나 해제해도 뒤따르는 Item은 삭제되지 않습니다.

부저 제어, 부저 재발생, 물리적 Reset 감지는 추가하지 않았습니다. 물리적 스위치 조작과 PC Queue 사이의 연결도 없습니다.
따라서 소프트웨어 Reset 버튼이나 가상의 부저 출력을 만들어 검증한 것으로 보고하지 않습니다.
Queue Clear는 기존 종료/내부 정리 용도만 남아 있습니다.

## 메모리와 화면 갱신

원본 이미지: 대기 큐 최대 Delay장 + 표시 대기 1장 + 현재 표시 1장 + 일시적 읽기/변환 이미지.
썸네일: 최대 Delay+1장(각각 최대 160×100), UI에는 독립 사본을 전달하고 교체/종료 시 Dispose.
파일 경로 중복 방지 구조는 기존 코드를 유지했습니다. 과거 원본 이미지를 메모리에 재생하지 않습니다.
UI가 바쁘면 기존처럼 최신 화면으로 갱신을 합치지만 모든 파일은 큐에 각각 들어갑니다.

## 검증

- 단일 NG, 연속 2/3/10개 NG 각각 정확한 도착 순번 검증.
- 지연 0/12/13/14/15 각각 확인. Delay=13에서 #1은 입력 #14에 도착.
- 이전 NG 표시 해제 후 다음 NG와 남은 큐 보존.
- OK만 입력하면 NG 도착 목록이 비어 있음.
- 파일명 NG 이미지의 반복 폴더 스캔에서 중복 입력/순서 이동 없음.
- 2,000개 입력 후 원본/썸네일 개수 제한, 종료 후 Dispose 및 독립 UI 사본 수명 확인.
- 기존 FTP 저장 중 재시도, 날짜 폴더 변경, 과거 파일 제외, SDK 설정 전달 테스트 유지.
- 실제 카메라/부저를 작동시키지 않고 검증. 물리적 Reset/부저 시험은 작업 범위에서 제외.
- Debug 및 Release 빌드 성공. 1~4대 × 5개 창 크기(800×600~3840×2160) 배치 및 자식 프로세스 정상 종료 테스트 통과.
