# SDK 확인 기록

조사 대상: 설치된 VisionMaster4.4.50의 Development/V4.x/ComControls/Assembly DLL,
PlatformSDKSampleCS.zip의 GetResultControl 예제, .NET V4.4.30 CHM.
버전 차이를 이유로 API를 추정하지 않고 설치 DLL reflection과 실제 컴파일로 서명을 확인했습니다.

| 항목 | 확인 결과 / 구현 |
|---|---|
| 검사 완료 대상 | 기존 Viewer는 Frontend 표시만 합니다. 새 업체 Procedure는 사용자 확인 결과 미정입니다. config에서 지정합니다. 이미지 소스 콜백을 완성된 검사로 간주하지 않습니다. |
| 콜백 | VmProcedure가 VmModule의 EnableResultCallback(), ModuleResultCallBackArrived(System.EventHandler)를 제공합니다. 지정 Procedure 하나만 구독합니다. |
| 이미지 | ProcedureResult.GetOutputImageV2(string) → VM.PlatformSDKCS.ImageBaseData. ToBitmap() → Bitmap. |
| 판정 | ProcedureResult.GetOutputInt(string) → IntDataArray(nValueNum, pIntVal). 출력 1개와 명시한 OK/NG 값만 허용합니다. ErrorCode는 SDK 실행 오류 검출에만 사용합니다. |
| 복사 / 수명 | DynamicOutput.GetOutputImageV2 IL에서 새 ImageBaseData 생성과 공유 메모리 경로를 확인했습니다. 콜백 안에서 ToBitmap 후 new Bitmap으로 복사합니다. SDK 객체는 명시적으로 Dispose합니다. Dispose() 메서드는 있지만 IDisposable은 구현하지 않는 타입입니다. |
| 동시 표시 | Frontend를 유지하며 지정 Procedure의 결과 보고만 활성화합니다. 전역 DisableModulesCallback, Run, Stop, Load를 호출하지 않습니다. 실제 카메라 동시 동작은 현장 검증이 남아 있습니다. |
| 콜백 번호 | VMControls.Interface.ValueEventArgs.ExecuteCount 공개 필드가 존재합니다. 0이 아닌 번호를 제공할 때 중복·불연속 감지에 사용합니다. UI Sequence는 초기화 이후 수신한 검사 순번입니다. |

공식 GetResultControl은 ModuleResultCallBackArrived 안에서 ModuResult를 읽고,
Procedure 결과 예제는 GetOutputImageV2를 사용합니다. CHM 이벤트 설명은 결과 콜백 안에서
데이터를 취득할 수 있으며 오래 걸리는 동기 작업은 다음 실행을 막을 수 있다고 명시합니다.
이미지와 판정은 같은 Procedure 콜백 안에서 복사하고 UI에 SDK 객체를 넘기지 않습니다.
최종 이미지와 판정을 같은 제품 흐름의 출력으로 구성하는 것은 현장 Solution의 전제입니다.

확인한 정적 이벤트: VmSolution.OnSolutionLoadBeginEvent, OnSolutionLoadEndEvent,
OnServerStatusEvent, OnProxyCrashEvent, OnProcedureUnRegisterEvent.
VmSolution.InstanceChanged는 인스턴스 이벤트입니다.
서버 상태 이벤트 문서는 서버 종료/단절 알림을 설명하며 단일 프로세스 버전에서는 발생하지 않는다고
기재합니다. Remote SC6000 케이블 단절·외부 Solution 전환에서 알림이 누락되지 않는지 검증은 별도입니다.
알림이 오지 않는 무통신 상태를 시간으로 추정해서 정상 검사처럼 처리하지 않습니다.

기존 VM.Core, VM.PlatformSDKCS, VMControls.BaseInterface, VMControls.Interface,
VMControls.Winform.Release를 참조합니다. 이미지의 IImageData 의존성 때문에 같은 설치의
VMControls.RenderInterface를 추가했습니다. 실제 설치 DLL 참조이며 Copy Local=false입니다.

주요 새 파일: InspectionSession.cs(SDK·세션), InspectionBuffer.cs(이미지 소유권·횟수 큐),
DelayedPane.cs(UI), MonitorLog.cs(로그). Program/ViewerForm에는 설정과 패널 연결을 추가했습니다.
