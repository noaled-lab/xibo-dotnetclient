# NOA Player MSI 인스톨러 빌드 가이드

## 개요

이 가이드는 NOA Player를 MSI 설치 패키지로 빌드하는 방법을 설명합니다.

## 설치 결과

MSI 인스톨러를 실행하면:
- **제품명**: "NOA Player"로 제어판에 표시
- **설치 경로**: `C:\Program Files\NOA Player\Player\`
- **시작 메뉴**: "NOA Player" 폴더에 다음 바로가기 생성:
  - **NOA Player Options**: 설정 창 실행 (XiboClient.exe o)
  - **NOA Player**: 플레이어 실행 (XiboClient.exe)

## 필수 요구사항

1. **WiX Toolset v3.11** 이상
2. **Visual Studio Build Tools** 또는 MSBuild
3. **.NET Framework 4.8** 개발자 팩

## WiX Toolset 설치

### 방법 1: winget 사용 (권장)

```powershell
winget install WiXToolset.WiXToolset
```

### 방법 2: 수동 설치

1. https://wixtoolset.org/releases/ 접속
2. WiX Toolset v3.11 다운로드
3. 설치 프로그램 실행

## MSI 인스톨러 빌드

### 단계 1: 클라이언트 빌드

```powershell
cd D:\xibo-cms\xibo-dotnetclient
.\build.ps1
```

### 단계 2: MSI 패키지 생성

```powershell
.\build-installer.ps1
```

이 스크립트는:
1. 클라이언트를 자동으로 빌드
2. WiX를 사용하여 MSI 패키지 생성
3. 출력 파일: `NOA-Player-v4.405.3.msi`

## 빌드 결과

- **MSI 파일**: `NOA-Player-v4.405.3.msi`
- **위치**: `D:\xibo-cms\xibo-dotnetclient\`

## 설치 테스트

1. MSI 파일을 더블클릭하여 설치
2. 제어판 > 프로그램 제거에서 "NOA Player" 확인
3. 시작 메뉴에서 "NOA Player" 폴더 확인
4. "NOA Player Options" 바로가기 실행하여 설정 창 확인
5. "NOA Player" 바로가기 실행하여 플레이어 확인

## 문제 해결

### WiX Toolset을 찾을 수 없음

- WiX Toolset이 설치되어 있는지 확인
- 설치 경로 확인: `C:\Program Files (x86)\WiX Toolset v3.11\bin\`

### MSI 빌드 실패

- 클라이언트가 먼저 빌드되었는지 확인
- `bin\Release` 폴더에 필요한 파일들이 있는지 확인
- WiX 로그 파일 확인

## 참고사항

- MSI 인스톨러는 관리자 권한이 필요합니다
- 기존 설치가 있으면 업그레이드로 처리됩니다
- 모든 DLL과 의존성 파일이 자동으로 포함됩니다

