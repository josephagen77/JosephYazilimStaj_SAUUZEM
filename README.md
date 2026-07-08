# MiniLMS AI

AI destekli Learning Management System (LMS)

ASP.NET Core MVC tabanlı, Gemini AI ve Qdrant Vector Database kullanan akıllı öğrenme yönetim sistemi.

## Project Overview

MiniLMS AI is an intelligent Learning Management System developed with ASP.NET Core MVC.

The system allows teachers to upload course materials while students can enroll in courses and interact with AI-powered course content.

Instead of performing keyword searches, the application generates semantic embeddings using Google's Gemini Embedding API and stores them in Qdrant Vector Database, enabling semantic search and question answering.
## Features

- Student Authentication
- Teacher Authentication
- Role-based Authorization
- Course Management
- Lesson Management
- Lesson Content Management
- Document Upload
- AI-powered Content Summarization
- Semantic Search
- Vector Database Integration
- Automatic Vector Indexing
- Background Processing
- Enrollment Management

  ## Technologies

- ASP.NET Core MVC
- Entity Framework Core
- SQL Server
- ASP.NET Identity
- AutoMapper
- Repository Pattern
- Service Layer Pattern
- Google Gemini API
- Qdrant Vector Database
- REST API

   Presentation (MVC)
│
├── Controllers
├── Views
├── ViewModels
│
Business Layer
│
├── Services
├── Interfaces
│
Data Access Layer
│
├── Repositories
├── Generic Repository
├── Entity Framework Core
│
Database
│
SQL Server
│
AI Layer
│
Gemini API
│
Vector Layer
│
Qdrant

MiniLMS
│
├── Controllers
├── Data
├── Interfaces
├── Mappings
├── Middlewares
├── Models
├── Repositories
├── Services
├── ViewModels
├── Views
├── Program.cs

## Installation

1. Clone repository

git clone ...

2. Install packages

dotnet restore

3. Update database

dotnet ef database update

4. Configure Gemini API Key

User Secrets

5. Run Qdrant

Docker

6. Start project

dotnet run
