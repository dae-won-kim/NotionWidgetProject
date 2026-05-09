# NotionWidget — Notion 연동 데스크톱 할 일 위젯

Notion 데이터베이스를 실시간으로 조회하고, 작업 상태를 UI에서 직접 변경할 수 있는 Windows 데스크톱 위젯입니다.

---

## 목차

1. [프로젝트 개요](#1-프로젝트-개요)
2. [기술 스택](#2-기술-스택)
3. [시스템 아키텍처](#3-시스템-아키텍처)
4. [디렉터리 구조](#4-디렉터리-구조)
5. [API 명세](#5-api-명세)
6. [데이터 모델](#6-데이터-모델)
7. [주요 기능](#7-주요-기능)
8. [개발 환경 설정](#8-개발-환경-설정)
9. [환경 변수](#9-환경-변수)

---

## 1. 프로젝트 개요

| 항목 | 내용 |
|------|------|
| **목적** | Notion 데이터베이스의 할 일 항목을 데스크톱 위젯으로 표시·관리 |
| **대상 OS** | Windows 10/11 |
| **사용자** | Notion을 업무 도구로 사용하는 개인 사용자 |
| **언어 지원** | 한국어 (UI 및 상태 레이블) |

사용자는 별도로 Notion을 열지 않고, 시스템 트레이에 상주하는 위젯을 통해 오늘의 할 일 목록을 확인하고 완료 처리할 수 있습니다.

---

## 2. 기술 스택

### 데스크톱 앱 (`apps/widget-desktop`)

| 항목 | 버전/세부 내용 |
|------|---------------|
| 언어 | C# |
| 런타임 | .NET 9.0 |
| UI 프레임워크 | [Avalonia UI](https://avaloniaui.net/) 11.3.10 |
| UI 테마 | Avalonia.Themes.Fluent |
| 폰트 | Inter (영문) + Malgun Gothic (한글) |
| 빌드 출력 | WinExe (Windows 실행 파일) |

### 백엔드 API (`services/widget-api`)

| 항목 | 버전/세부 내용 |
|------|---------------|
| 언어 | C# |
| 런타임 | .NET 9.0 |
| 프레임워크 | ASP.NET Core Minimal API |
| 외부 의존성 | Notion API v1 (`2022-06-28`) |
| HTTP 클라이언트 | .NET 내장 `HttpClient` |

---

## 3. 시스템 아키텍처

### 컴포넌트 다이어그램

```
┌─────────────────────────────────────────────────────────────┐
│                     Windows 사용자 환경                      │
│                                                             │
│   ┌────────────────────────┐                                │
│   │   widget-desktop       │                                │
│   │  (Avalonia WinExe)     │                                │
│   │                        │                                │
│   │  ┌──────────────────┐  │                                │
│   │  │   MainWindow     │  │                                │
│   │  │  - 할 일 목록 렌더링  │  │                                │
│   │  │  - 상태 변경 UI    │  │                                │
│   │  │  - 드래그앤드롭 정렬 │  │                                │
│   │  └────────┬─────────┘  │                                │
│   │           │             │                                │
│   │  ┌────────▼─────────┐  │                                │
│   │  │  WidgetApiClient │  │                                │
│   │  │  (HTTP 클라이언트)  │  │                                │
│   │  └────────┬─────────┘  │                                │
│   └───────────┼────────────┘                                │
│               │ HTTP (localhost:5183)                        │
│   ┌───────────▼────────────┐                                │
│   │    widget-api           │                                │
│   │  (ASP.NET Core)         │                                │
│   │                         │                                │
│   │  - 항목 조회 / 상태 변경  │                                │
│   │  - Notion 응답 파싱·정규화│                                │
│   │  - 상태 옵션 인메모리 캐시 │                                │
│   └───────────┬─────────────┘                                │
└───────────────┼─────────────────────────────────────────────┘
                │ HTTPS (api.notion.com)
   ┌────────────▼──────────────┐
   │       Notion API           │
   │   (외부 서비스)             │
   │   - 데이터베이스 조회        │
   │   - 페이지 속성 업데이트     │
   └───────────────────────────┘
```

### 데이터 흐름 (할 일 목록 조회)

```
Desktop App                widget-api               Notion API
    │                          │                         │
    │  POST /v1/widgets/       │                         │
    │  {widgetId}/items/query  │                         │
    │ ────────────────────────►│                         │
    │                          │  POST /v1/databases/    │
    │                          │  {dbId}/query           │
    │                          │ ───────────────────────►│
    │                          │                         │
    │                          │  Notion 페이지 목록 반환  │
    │                          │ ◄───────────────────────│
    │                          │                         │
    │                          │  [파싱 & 정규화]         │
    │                          │  - 제목 추출             │
    │                          │  - 상태/색상 매핑         │
    │                          │  - 요일 다중선택 파싱      │
    │                          │                         │
    │  { ok, data: { items,    │                         │
    │    statusOptions } }     │                         │
    │ ◄────────────────────────│                         │
    │                          │                         │
```

### 작업 상태 흐름

```
        ┌─────────┐
        │ 시작 전  │  ──────►  진행 중  ──────►  완료
        └─────────┘             (순환: 완료 → 시작 전)
```

상태 전환은 두 가지 방식으로 실행됩니다:
- **다음 상태 버튼**: 현재 순서에서 다음 상태로 자동 이동
- **상태 직접 선택**: 드롭다운 메뉴에서 원하는 상태 직접 지정

---

## 4. 디렉터리 구조

```
NotionWidgetProject/
├── apps/
│   └── widget-desktop/              # Avalonia 데스크톱 앱
│       ├── Converters/              # UI 바인딩용 값 변환기
│       │   ├── NotionColorToBrushConverter.cs
│       │   └── NotionColorToFgBrushConverter.cs
│       ├── Models/                  # DTO (API 응답 매핑 클래스)
│       │   ├── ItemDto.cs           # 할 일 항목 (INotifyPropertyChanged)
│       │   ├── QueryItemsResponseDto.cs
│       │   ├── StatusOptionDto.cs
│       │   └── StatusUpdateResponseDto.cs
│       ├── Services/
│       │   └── WidgetApiClient.cs   # widget-api HTTP 클라이언트
│       ├── Styles/
│       │   ├── WidgetTheme.cs       # 색상·폰트 상수
│       │   └── WidgetStyles.axaml   # XAML 스타일
│       ├── App.axaml / App.axaml.cs # 앱 진입점, 트레이 아이콘
│       ├── MainWindow.axaml         # 메인 창 레이아웃
│       ├── MainWindow.axaml.cs      # 메인 창 로직 (607줄)
│       └── widget-desktop.csproj
│
├── services/
│   └── widget-api/                  # ASP.NET Core Minimal API
│       ├── Program.cs               # 모든 엔드포인트 정의 (175줄)
│       ├── appsettings.json         # 로깅 및 Notion 자격증명 설정
│       └── widget-api.csproj
│
├── shared/
│   └── contracts/                   # 공유 인터페이스 (현재 미사용, 확장 예약)
│
└── README.md
```

---

## 5. API 명세

**Base URL:** `http://localhost:5183`

---

### `GET /`

헬스 체크

**응답 예시:**
```json
{ "app": "widget-api", "ok": true }
```

---

### `GET /health`

상세 헬스 체크

**응답 예시:**
```json
{ "ok": true }
```

---

### `POST /v1/widgets/{widgetId}/items/query`

Notion 데이터베이스에서 할 일 항목 목록과 상태 옵션을 조회합니다.

**Path Parameters:**

| 파라미터 | 타입 | 설명 |
|----------|------|------|
| `widgetId` | string | 위젯 식별자 (현재 `w_1` 고정) |

**응답 `200 OK`:**
```json
{
  "ok": true,
  "data": {
    "items": [
      {
        "id": "notion-page-id",
        "title": "작업 제목",
        "status": "진행 중",
        "statusId": "notion-status-option-id",
        "statusColor": "blue",
        "days": ["월요일", "수요일"],
        "note": "메모 내용",
        "lastEditedTime": "2026-05-09T10:00:00.000Z"
      }
    ],
    "statusOptions": [
      { "id": "option-id", "name": "시작 전", "color": "gray" },
      { "id": "option-id", "name": "진행 중", "color": "blue" },
      { "id": "option-id", "name": "완료",   "color": "green" }
    ]
  }
}
```

---

### `POST /v1/widgets/{widgetId}/items/{itemId}/status/next`

항목의 상태를 다음 순서로 변경합니다.  
순서: **시작 전 → 진행 중 → 완료 → 시작 전** (순환)

**Path Parameters:**

| 파라미터 | 타입 | 설명 |
|----------|------|------|
| `widgetId` | string | 위젯 식별자 |
| `itemId` | string | Notion 페이지 ID |

**응답 `200 OK`:**
```json
{
  "ok": true,
  "data": {
    "id": "notion-page-id",
    "statusId": "new-status-option-id",
    "status": "진행 중",
    "lastEditedTime": "2026-05-09T10:01:00.000Z"
  }
}
```

---

### `PATCH /v1/widgets/{widgetId}/items/{itemId}/status`

항목의 상태를 지정한 상태로 직접 변경합니다.

**Path Parameters:**

| 파라미터 | 타입 | 설명 |
|----------|------|------|
| `widgetId` | string | 위젯 식별자 |
| `itemId` | string | Notion 페이지 ID |

**Request Body:**
```json
{ "statusId": "notion-status-option-id" }
```

**응답 `200 OK`:**
```json
{
  "ok": true,
  "data": {
    "id": "notion-page-id",
    "statusId": "notion-status-option-id",
    "status": "완료",
    "lastEditedTime": "2026-05-09T10:02:00.000Z"
  }
}
```

---

### 오류 응답 공통 형식

```json
{ "ok": false, "error": "오류 메시지" }
```

---

## 6. 데이터 모델

### ItemDto — 할 일 항목

| 필드 | 타입 | 설명 |
|------|------|------|
| `Id` | string | Notion 페이지 ID |
| `Title` | string | 작업 제목 |
| `Status` | string | 현재 상태 이름 (`시작 전` / `진행 중` / `완료`) |
| `StatusId` | string | Notion 상태 옵션 ID |
| `StatusColor` | string | 정규화된 색상 (`blue` / `green` / `yellow` / `red` / `gray`) |
| `Days` | List\<string\> | 연관 요일 목록 (`월요일` ~ `일요일`) |
| `Note` | string | 메모 텍스트 |
| `LastEditedTime` | string | Notion 마지막 수정 시각 (ISO 8601) |
| `UiOrder` | int | 드래그앤드롭으로 조정된 화면 순서 |
| `IsChecked` | bool | 완료 여부 (UI 바인딩용 computed) |

> `ItemDto`는 `INotifyPropertyChanged`를 구현하여 상태 변경 시 UI가 즉시 갱신됩니다.

### StatusOptionDto — 상태 옵션

| 필드 | 타입 | 설명 |
|------|------|------|
| `Id` | string | Notion 상태 옵션 ID |
| `Name` | string | 상태 이름 |
| `Color` | string | 정규화된 색상 |

### 색상 정규화 매핑

Notion의 다양한 색상 이름을 앱 내 5가지 팔레트로 통일합니다.

| 앱 색상 | 배경색 | 전경색 | 해당 Notion 색상 |
|---------|--------|--------|-----------------|
| `blue` | `#7AB8E8` | `#0C2E50` | blue, blue_background |
| `green` | `#72CFA0` | `#0F4028` | green, teal, green_background, teal_background |
| `yellow` | `#F0C456` | `#4A3000` | yellow, orange, brown, yellow_background, orange_background |
| `red` | `#F49494` | `#4A1010` | red, pink, purple, red_background, pink_background, purple_background |
| `gray` | `#AABAC8` | `#2E3F4F` | gray, default, 그 외 모두 |

---

## 7. 주요 기능

| 기능 | 설명 |
|------|------|
| **요일별 필터링** | 월~일 버튼으로 오늘의 할 일만 표시 |
| **상태 순환** | 항목 우측 버튼 클릭 시 다음 상태로 전환 |
| **상태 직접 선택** | 상태 뱃지 클릭 → 드롭다운에서 원하는 상태 선택 |
| **드래그앤드롭 정렬** | 항목을 잡아 원하는 순서로 이동 (FLIP 애니메이션) |
| **다크/라이트 모드** | 상단 토글 버튼 또는 좌우 스와이프 제스처로 전환 |
| **시스템 트레이** | 트레이 아이콘으로 숨기기/보이기, 시작 시 자동 실행 설정 |
| **창 위치 저장** | 앱 종료 후 재실행 시 이전 위치·크기 복원 |
| **프레임리스 창** | 제목 표시줄 없는 커스텀 창 크롬, 모든 가장자리 리사이즈 |

---

## 8. 개발 환경 설정

### 사전 요구사항

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Notion API 토큰 및 연동할 데이터베이스 ID

### Notion 데이터베이스 스키마 요구사항

widget-api가 올바르게 파싱하려면 Notion 데이터베이스에 아래 속성이 존재해야 합니다.

| 속성 이름 | Notion 속성 타입 | 필수 여부 |
|-----------|----------------|-----------|
| `Task` | Title | 필수 |
| `Status` | Status | 필수 |
| `Days` | Multi-select | 선택 |
| `Note` | Rich Text | 선택 |

### 실행 방법

**1. API 서버 시작**

```powershell
cd services/widget-api
$env:Notion__Token    = "secret_xxxxxxxxxxxxxxxx"
$env:Notion__DatabaseId = "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
dotnet run
# → http://localhost:5183 에서 실행
```

**2. 데스크톱 앱 실행**

```powershell
cd apps/widget-desktop
dotnet run
```

> API 서버가 먼저 실행 중이어야 합니다.

---

## 9. 환경 변수

### widget-api

| 변수 | 필수 | 기본값 | 설명 |
|------|------|--------|------|
| `Notion__Token` | 필수 | — | Notion Internal Integration Token |
| `Notion__DatabaseId` | 필수 | — | 연동할 Notion 데이터베이스 ID |

### widget-desktop

| 변수 | 필수 | 기본값 | 설명 |
|------|------|--------|------|
| `WIDGET_API_BASE_URL` | 선택 | `http://localhost:5183` | widget-api 주소 |

> 창 위치·크기 설정은 `%APPDATA%\NotionWidget\window.json`에 자동 저장됩니다.
