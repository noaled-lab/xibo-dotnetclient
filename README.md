### 미디어 다운로드 관련 참고사항
Xibo는 2000년대부터 개발 시작된 완전 오래된 프로젝트임.
당시에는 SOAP를 주로 썼던걸로 보이고 청크 다운로드가 구현되어 있음

CMS 쪽에 bytesRequested라는게 있는데 이건 플레이어에게 요청된 다운로드 크기임
SOAP 청크 다운로드에서는 잘 동작했던 것으로 추정함

---

## NOA Player — libmpv 렌더러 (VideoMpv)

이 포크에는 기본 WPF `MediaElement` 대신 **libmpv**를 사용하는 `VideoMpv` 렌더러가 포함되어 있습니다.  
`config.xml`에서 `<VideoRenderingEngine>libmpv</VideoRenderingEngine>`으로 활성화합니다.

### 빌드 방법

```powershell
# 프로세스 종료 → 빌드 → 자동 실행 → 30초 후 로그 출력
.\scripts\build-and-test.ps1

# 대기 시간 조정 (예: 60초)
.\scripts\build-and-test.ps1 -WaitSeconds 60
```

빌드 결과물: `bin\x86\Release\XiboClient.exe`  
실행 시 현재 디렉터리(`bin\x86\Release`)를 working directory로 사용합니다.

### 로그 파일 위치

| 항목 | 경로 |
|------|------|
| 라이브러리/설정 폴더 | `C:\xibo2\` |
| 로그 파일 | `C:\xibo2\log.xml_<타임스탬프>` (예: `log.xml_134195027966253375`) |
| config.xml | `C:\xibo2\config.xml` |

> **주의:** 로그는 프로세스 실행 중 파일이 잠기므로 **반드시 프로세스 종료 후** 읽어야 합니다.  
> 최신 로그 확인: `ls -t C:\xibo2\log.xml_* | head -1`

MPV/Video 상세 로그를 보려면 `config.xml`의 `<LogLevel>`을 `audit`으로 설정하세요.

---

## [버그 수정] 영상 재생 시작 시 회색 플래시(Grey Flash) 문제

### 증상

libmpv 렌더러로 영상을 재생할 때, 영상이 처음 시작되는 순간과 영상 전환 시 **회색 화면이 순간적으로 깜빡이는** 현상이 발생했습니다. 재생 영역 크기와 정확히 일치하는 회색 사각형이 표시되었습니다.

### 원인 분석

**핵심 원인**: WPF `HwndHost`로 mpv Win32 창을 임베드할 때, mpv GPU 렌더러(DirectX)가 VO(Video Output)를 초기화하는 과정에서 swap chain을 회색(D3D 기본 clear color)으로 한 프레임 그린 뒤 첫 번째 비디오 프레임을 출력합니다.

시도했지만 효과 없었던 방법들:

| 시도 | 결과 | 이유 |
|------|------|------|
| WPF `Visibility.Hidden` | 효과 없음 | WPF Visibility는 HwndHost의 Win32 창을 실제로 숨기지 못함 |
| WPF `Border` 검정 오버레이 | 효과 없음 | WPF Airspace 제약 — HwndHost(Win32)는 항상 WPF 요소 위에 그려짐 |
| mpv `background=#000000` 옵션 | 효과 없음 | VO 초기화 완료 전에는 mpv가 이 옵션을 적용하지 못함 |
| `WM_ERASEBKGND`를 검정으로 처리 | 부분적 | WM_ERASEBKGND는 막을 수 있지만 mpv GPU 렌더러는 GDI를 우회함 |
| `WM_PAINT`를 검정으로 처리 | 효과 없음 | mpv DirectX 렌더러는 WM_PAINT를 무시하고 D3D swap chain으로 직접 그림 |
| `VIDEO_RECONFIG` 이벤트에서 오버레이 제거 | 효과 없음 | HwndHost 위에 WPF 오버레이 자체가 표시되지 않음 |

### 해결책

**Win32 `ShowWindow`로 창 가시성을 직접 제어합니다.**

1. `CreateWindowEx` 호출 시 **`WS_VISIBLE` 플래그를 제외**하여 창을 숨긴 상태로 생성합니다.
   - 이 시점에 mpv GPU 렌더러가 초기화되어 회색으로 클리어해도 창이 보이지 않습니다.
2. mpv 이벤트 루프에서 **`MPV_EVENT_VIDEO_RECONFIG`** 이벤트를 수신하면 `ShowWindow(SW_SHOW)`를 호출합니다.
   - `VIDEO_RECONFIG`는 mpv VO가 완전히 구성되어 첫 비디오 프레임을 출력할 준비가 된 시점입니다.
3. 다음 파일 로드 시(`Load()` 호출)에는 `ShowWindow(SW_HIDE)`로 다시 숨겨 전환 시에도 동일하게 동작합니다.

### mpv 이벤트 순서 (확인됨)

```
START_FILE → FILE_LOADED → eof-reached=0 → VIDEO_RECONFIG → VIDEO_RECONFIG
```

- `FILE_LOADED`: 파일 메타데이터 로드 완료. VO는 아직 미구성.
- `VIDEO_RECONFIG` (첫 번째): VO 구성 완료. 이 시점에 `ShowWindow`로 창을 표시.
- `VIDEO_RECONFIG`가 두 번 연속 발생하므로 첫 번째에서만 처리 (`_videoReady` 플래그).

### 관련 파일

| 파일 | 변경 내용 |
|------|-----------|
| [Rendering/MpvHost.cs](Rendering/MpvHost.cs) | `WS_VISIBLE` 제거, `ShowWindow` P/Invoke 추가, `VIDEO_RECONFIG`에서 SW_SHOW, `Load()`에서 SW_HIDE |
| [Rendering/VideoMpv.cs](Rendering/VideoMpv.cs) | libmpv 기반 Video 렌더러 (Video.cs 대체) |
| [Rendering/LibMpv.cs](Rendering/LibMpv.cs) | libmpv P/Invoke 바인딩 (`mpv_observe_property`, 이벤트 상수 등) |
| [scripts/build-and-test.ps1](scripts/build-and-test.ps1) | 프로세스 종료 → 빌드 → 실행 → 로그 출력 자동화 스크립트 |

### 추가 실험 결과 (가드 OFF 옵션 비교)

가시성 타이밍 가드(`USE_WINDOW_TIMING_GUARD`)를 끈 상태에서 VO 옵션만 바꿔 A/B 테스트한 결과:

| 설정 | 결과 |
|------|------|
| `vo=gpu` | 회색 플래시 재현 |
| `vo=direct3d` | 대부분 완화되지만 환경/상황에 따라 간헐 재현 가능 |
| `vo=gpu,direct3d` | 환경에 따라 `gpu`가 먼저 선택되어 회색 플래시가 재현될 수 있음 |

정리:

- `background` 옵션은 알파 블렌딩 배경 동작(`color`/`tiles`/`none`) 제어이며, 초기 VO 생성 타이밍 자체를 보장 제어하지는 못한다.
- `vo=direct3d` 고정은 재현 빈도를 줄일 수 있지만, **완전한 방지는 보장하지 못했다**.
- 따라서 본 이슈는 렌더러 종류와 무관하게 **타이밍 가드(`ShowWindow` 기반 hide/show)를 항상 사용하는 것이 필수**다.
- 실무 권장값: `MpvUseTimingGuard=true`를 기본으로 두고, VO 옵션은 성능/호환성 요구에 맞게 별도 선택한다.

### EOF/루프 처리 (keep-open)

`MediaElement`의 루프 방식을 참고하여 `keep-open: yes`로 설정한 뒤 EOF 감지 시 `SeekToStart()` + `Play()`로 루프를 구현합니다.  
`keep-open: no`(기본값)를 사용하면 EOF 후 mpv가 파일을 언로드하여 `SeekToStart()`가 실패하고 회색 화면이 됩니다.

---

ReportInventory는 플레이어가 현재 파일의 상태를 보고하는 메서드임
기존에는 다운로드 시작 시, 그리고 다운로드 완료 시 보고하는 것으로 되어 있음
이를 수정하여 기존에 없던 다운로드 크기를 추가하였음

HTTP 다운로드를 비동기로 변경하고 이벤트를 활용하여
다운로드 과정을 추적하도록 함

다운로드 과정에서 조건에 맞을 경우 ReportInventory를 호출하도록 하였음
너무 자주 서버에 요청하면 CMS에 부하가 걸릴 수 있으니 일정 시간 딜레이를 두었음

*CMS 설정으로 SOAP와 REST 중 선택할 수 있음. 도커로 실행하면 기본 REST임
