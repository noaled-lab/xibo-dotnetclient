# CMS 서버 빌드 및 배포 가이드

## 개요
고객 서버 PC에 CMS를 설치하기 위한 Docker 이미지 빌드 및 배포 방법을 안내합니다.

---

## 1단계: 개발자 PC에서 Docker 이미지 빌드

### 1-1. CMS 프로젝트 디렉토리로 이동
```powershell
cd D:\xibo-cms
```

### 1-2. Docker 이미지 빌드
```powershell
# 프로덕션 이미지 빌드
docker build -t noa-cms:latest -f Dockerfile .

# 또는 docker-compose 사용
docker-compose build
```

### 1-3. 이미지 저장
```powershell
# 이미지를 tar 파일로 저장
docker save noa-cms:latest -o noa-cms-latest.tar

# 파일 크기 확인
Get-Item noa-cms-latest.tar | Select-Object Name, @{Name="Size(MB)";Expression={[math]::Round($_.Length/1MB,2)}}
```

**예상 파일 크기:** 500MB ~ 2GB (이미지 내용에 따라 다름)

---

## 2단계: 배포 패키지 준비

### 2-1. 배포 폴더 생성
```
CMS-서버-배포패키지/
├── CMS-서버-설치-프로그램.ps1
├── CMS-서버-설치.bat
├── noa-cms-latest.tar (Docker 이미지)
└── README.txt (설치 안내)
```

### 2-2. README.txt 작성 예시
```
NOA CMS 서버 설치 패키지
========================

필수 요구사항:
- Windows 10 이상
- Docker Desktop 설치 필요
  다운로드: https://www.docker.com/products/docker-desktop

설치 방법:
1. Docker Desktop 설치 및 실행
2. CMS-서버-설치.bat 실행
3. 설치 완료 후 브라우저에서 http://localhost:8000 접속

기본 로그인 정보:
- 사용자명: xibo_admin
- 비밀번호: password

플레이어 설정:
- CMS Address: http://[서버IP]:8000/xmds.php
- 서버 IP 확인: ipconfig 명령어 사용
```

---

## 3단계: 고객 서버 PC에 배포

### 3-1. 배포 패키지 전달
- USB 드라이브, 네트워크 공유, 클라우드 스토리지 등으로 전달

### 3-2. 설치 실행
1. **Docker Desktop 설치** (아직 설치되지 않은 경우)
   - https://www.docker.com/products/docker-desktop
   - 설치 후 Docker Desktop 실행

2. **CMS 설치 프로그램 실행**
   - `CMS-서버-설치.bat` 더블클릭
   - 또는 PowerShell에서:
     ```powershell
     .\CMS-서버-설치-프로그램.ps1
     ```

3. **설치 과정**
   - Docker 확인
   - Docker 이미지 로드 (시간 소요)
   - 데이터 디렉토리 생성
   - Docker Compose 설정 생성
   - 서비스 시작
   - 서버 IP 확인

### 3-3. 설치 확인
- 브라우저에서 `http://localhost:8000` 접속
- CMS 로그인 페이지가 표시되는지 확인

---

## 4단계: 플레이어 설정

### 4-1. 서버 IP 주소 확인
```batch
# 서버 PC에서 실행
ipconfig

# IPv4 주소 확인 (예: 192.168.1.100)
```

### 4-2. 플레이어 PC에서 설정
1. 플레이어 설치
2. Options 창에서:
   - **CMS Address**: `http://192.168.1.100:8000/xmds.php` (서버 IP 사용)
   - **CMS Key**: CMS 관리자 페이지에서 확인
3. Connect 버튼 클릭

---

## 고급 옵션

### 이미지 파일 없이 설치 (이미지가 이미 로드된 경우)
```powershell
.\CMS-서버-설치-프로그램.ps1 -SkipImageLoad
```

### 특정 이미지 파일 지정
```powershell
.\CMS-서버-설치-프로그램.ps1 -ImageFile "C:\path\to\noa-cms-latest.tar"
```

### 자동 설치 (대화형 입력 없음)
```powershell
.\CMS-서버-설치-프로그램.ps1 -Silent
```

---

## 문제 해결

### Docker 이미지 로드 실패
```powershell
# 이미지가 이미 로드되어 있는지 확인
docker images | findstr noa-cms

# 이미 로드되어 있다면 SkipImageLoad 옵션 사용
.\CMS-서버-설치-프로그램.ps1 -SkipImageLoad
```

### 서비스 시작 실패
```powershell
# 로그 확인
docker-compose logs

# 서비스 상태 확인
docker-compose ps

# 서비스 재시작
docker-compose restart
```

### 포트 충돌
- 포트 8000이 이미 사용 중인 경우:
  - `docker-compose.yml`에서 포트 변경
  - 예: `"8001:80"` (외부:내부)

### 방화벽 설정
- Windows 방화벽에서 포트 8000 허용 필요
- 고급 방화벽 설정에서 인바운드 규칙 추가

---

## 서비스 관리

### 서비스 시작
```batch
docker-compose up -d
```

### 서비스 중지
```batch
docker-compose down
```

### 서비스 재시작
```batch
docker-compose restart
```

### 로그 확인
```batch
docker-compose logs -f
```

### 서비스 상태
```batch
docker-compose ps
```

---

## 업데이트 방법

### 새 이미지로 업데이트
1. 새 이미지 파일 받기
2. 기존 서비스 중지: `docker-compose down`
3. 새 이미지 로드: `docker load -i noa-cms-new.tar`
4. 이미지 태그 변경: `docker tag noa-cms:latest noa-cms:old`
5. 새 이미지 태그: `docker tag [새이미지ID] noa-cms:latest`
6. 서비스 재시작: `docker-compose up -d`

---

## 배포 체크리스트

### 개발자 PC
- [ ] Docker 이미지 빌드 완료
- [ ] 이미지 tar 파일 생성 완료
- [ ] 배포 패키지 준비 완료
- [ ] README.txt 작성 완료

### 고객 서버 PC
- [ ] Docker Desktop 설치 완료
- [ ] Docker Desktop 실행 중
- [ ] 배포 패키지 복사 완료
- [ ] 설치 프로그램 실행 완료
- [ ] CMS 접속 확인 완료
- [ ] 서버 IP 주소 확인 완료

### 플레이어 PC
- [ ] 플레이어 설치 완료
- [ ] 서버 IP 주소로 설정 완료
- [ ] CMS 연결 확인 완료

---

## 참고사항

- Docker 이미지 파일은 용량이 클 수 있습니다 (500MB ~ 2GB)
- 이미지 로드에는 시간이 걸릴 수 있습니다 (5-10분)
- 서비스 시작 후 초기화에 시간이 걸릴 수 있습니다 (1-2분)
- 네트워크 환경에 따라 플레이어 연결이 안 될 수 있습니다 (방화벽 확인)

