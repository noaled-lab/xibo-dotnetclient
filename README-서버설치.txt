NOA CMS 서버 설치 패키지
========================

⭐ 서버 PC에서 이 파일을 실행하세요: CMS-서버-설치.bat

필수 요구사항:
- Windows 10 이상
- Docker Desktop 설치 필요
  다운로드: https://www.docker.com/products/docker-desktop

설치 방법 (매우 간단!):
1. Docker Desktop 설치 및 실행
2. CMS-서버-설치.bat 더블클릭 ← 이게 전부입니다!
3. 설치 완료 후 브라우저에서 http://localhost:8000 접속

기본 로그인 정보:
- 사용자명: xibo_admin
- 비밀번호: password

플레이어 설정:
- CMS Address: http://[서버IP]:8000/xmds.php
- 서버 IP 확인: ipconfig 명령어 사용
- CMS Key: CMS 관리자 페이지에서 확인

서비스 관리:
- 시작: docker-compose up -d
- 중지: docker-compose down
- 재시작: docker-compose restart
- 로그: docker-compose logs -f
- 상태: docker-compose ps

문제 해결:
- Docker Desktop이 실행 중인지 확인
- 방화벽에서 포트 8000 허용 확인
- 자세한 내용은 CMS-서버-빌드-및-배포-가이드.md 참고

