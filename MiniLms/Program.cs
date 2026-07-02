using Microsoft.EntityFrameworkCore;
using MiniLms.Data;
using MiniLms.Interfaces;
using MiniLms.Repositories;
using MiniLms.Services;
using MiniLms.Mappings;

var builder = WebApplication.CreateBuilder(args);

// 1. MVC Kontrolcü ve Görünüm Servislerini Ekle (Çift olan satırlardan biri temizlendi)
builder.Services.AddControllersWithViews();

// 2. Entity Framework Core ve SQL Server Veritabanı Bağlantısı
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// REPOSITORY VE SERVİS KAYITLARI (BAĞIMLILIK ENJEKSİYONU)

// Generic Repository Desteği
builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

// Öğrenci (Student) Katmanı Servisleri
builder.Services.AddScoped<IStudentRepository, StudentRepository>();
builder.Services.AddScoped<IStudentService, StudentService>();

// Ders (Course) Katmanı Servisleri 
builder.Services.AddScoped<ICourseRepository, CourseRepository>();
builder.Services.AddScoped<ICourseService, CourseService>();

// Ders Kayıtları (Enrollment) Katmanı Servisleri
builder.Services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();
builder.Services.AddScoped<IEnrollmentService, EnrollmentService>();

// Doküman Modülü Servisleri (Eksik repository kaydı eklendi)
builder.Services.AddScoped<ICourseDocumentRepository, CourseDocumentRepository>();
builder.Services.AddScoped<ICourseDocumentService, CourseDocumentService>();

// Haftalık Konular (Lesson) Katmanı Servisleri (Eksik olanlar eklendi)
builder.Services.AddScoped<ILessonRepository, LessonRepository>();
builder.Services.AddScoped<ILessonService, LessonService>();
builder.Services.AddScoped<ILessonContentRepository, LessonContentRepository>();

// HTTPCLIENT ENTEGRASYONLU AI VE VECTOR SERVİSLERİ

builder.Services.AddHttpClient<IAiService, AiService>();
builder.Services.AddHttpClient<IVectorDbService, VectorDbService>();
builder.Services.AddHostedService<VectorSyncService>();

// 4. AUTOMAPPER PROFİL HARİTALAMASINI KAYDET (HATAYI ÇÖZEN SATIR)
builder.Services.AddAutoMapper(typeof(Program));

var app = builder.Build();

// HTTP İstek İşleme Boru Hattı (Middleware Pipeline) Yapılandırması
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// Varsayılan Rota Tanımlaması (Uygulama Home/Index ile açılır)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();