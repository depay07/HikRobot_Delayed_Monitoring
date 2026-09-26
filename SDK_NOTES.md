# 입력 방식 변경 기록

현재 Delayed Monitor는 사용자가 지정한 PC FTP 수신 폴더를 읽습니다.
검사 1회당 저장 이미지 1개를 사용하며 저장 시간순으로 DELAY_COUNT개 전 이미지를 표시합니다.
그래픽과 OK/NG는 저장 이미지에 포함된 내용을 그대로 표시합니다.

이전 SDK 결과 콜백 방식은 제거했습니다.
InspectionSession.cs와 PROCEDURE, IMAGE_OUTPUT, RESULT_OUTPUT, OK_VALUE, NG_VALUE 설정은 사용하지 않습니다.
CameraStartup.cs에서 config의 IP/PORT/PASSWORD로 GetSolutionInstanceToDevice를 호출합니다. SOLUTION_PATH가 지정되면 공식 VmSolution.Load(path, password)로 자동 로딩합니다. 이미지 표시와 카메라 연결 상태를 분리하며 Run/Stop 명령은 호출하지 않습니다. 실제 카메라 접속 검증은 현장 설정 입력 후 필요합니다.

FolderImageSource.cs: 하위 날짜 폴더 검색, 저장 시간 정렬, 수신 완료 확인, 이미지 복사 및 횟수 큐.
InspectionBuffer.cs: 지연 이미지 소유권과 제한된 UI 갱신 대기 큐.
ViewerForm.cs: 폴더 읽기는 백그라운드, 화면 갱신은 WinForms UI 스레드에서 수행.
DelayedPane.cs: 저장 이미지 전체를 비율 유지하여 표시. 별도 판정값을 생성하지 않음.

폴더 검색 타이머는 새 파일을 발견하는 용도입니다. 지연은 파일 개수로만 계산합니다.
날짜 폴더가 바뀌어도 큐를 초기화하지 않습니다. 원본 이미지 파일은 수정하지 않습니다.