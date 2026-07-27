# MiniLMS AI

MiniLMS AI is an intelligent Learning Management System (LMS) built with ASP.NET Core MVC. The platform enables teachers to create and manage courses, lessons, and educational materials, while allowing students to enroll in courses and access learning content through a modern web interface.

The project integrates Google Gemini AI and Qdrant Vector Database to provide AI-powered features such as document summarization, semantic search, and intelligent question answering. Course materials are automatically processed, converted into vector embeddings, and indexed for fast and context-aware information retrieval.

## System Architecture

This project follows a standard **ASP.NET Core MVC Monolithic Architecture** with a logical N-tier separation of concerns to maintain code scalability and testability:

*   **Presentation Layer (Controllers & Views):** Manages the web interface, user routing, and data binding using Data Transfer Objects (ViewModels) to ensure data integrity.
*   **Business Logic Layer (Services):** Contains the core orchestration logic (e.g., `CourseService`, `AiService`), isolating business rules from data access and presentation.
*   **Data Access Layer (Repositories & Data):** Utilizes Entity Framework Core with SQL Server for relational data and implements the Repository Pattern (`GenericRepository`) for standardized data access.
*   **AI & Vector Infrastructure:** Integrates Google Gemini for embeddings and text generation, alongside a local Qdrant Vector DB container for high-dimensional semantic search indexing.

## Features

- Student & Teacher Authentication with Role-based Authorization
- Course, Lesson, and Content Management
- Document Upload & Background Processing
- AI-powered Content Summarization & Semantic Search
- Vector Database Integration & Automatic Indexing

## Technologies

- ASP.NET Core MVC & Entity Framework Core (SQL Server)
- ASP.NET Identity
- AutoMapper
- Repository & Service Layer Patterns
- Google Gemini API & Qdrant Vector Database

## Installation

1. Clone repository
```bash
git clone [https://github.com/josephagen77/JosephYazilimStaj_SAUUZEM.git](https://github.com/josephagen77/JosephYazilimStaj_SAUUZEM.git)