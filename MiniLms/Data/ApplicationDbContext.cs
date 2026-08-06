using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MiniLms.Models;

namespace MiniLms.Data
{
    // 🎯 KRİTİK DEĞİŞİKLİK: Standart DbContext yerine IdentityDbContext<ApplicationUser> entegre edildi.
    // Bu sayede hem öğretmen/öğrenci giriş tabloları hem de mevcut LMS tabloları tek bir veritabanında birleşir.
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // --- MEVCUT LMS TABLOLARI ---
        public DbSet<Course> Courses { get; set; }
        public DbSet<Lesson> Lessons { get; set; }
        public DbSet<LessonContent> LessonContents { get; set; }
        public DbSet<CourseDocument> CourseDocuments { get; set; }
        public DbSet<Enrollment> Enrollments { get; set; }
        public DbSet<Student> Students { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }

        // --- 🎯 YENİ: SİSTEM YÖNETİCİSİ, DEPARTMAN VE AI TABLOLARI ---
        public DbSet<Department> Departments { get; set; }
        public DbSet<AiProvider> AiProviders { get; set; }
        public DbSet<UserAiProvider> UserAiProviders { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            // 🎯 ÇOK KRİTİK: Identity tablolarının hatasız kurulması için base metodu MUTLAKA ilk satırda çağrılmalıdır.
            base.OnModelCreating(builder);

            // ==========================================
            // 1. MEVCUT İLİŞKİLER (Enrollment & Chat vb.)
            // ==========================================

            builder.Entity<Enrollment>().HasKey(e => e.Id);
            builder.Entity<Enrollment>().HasIndex(e => new { e.StudentId, e.CourseId }).IsUnique();

            builder.Entity<Enrollment>()
                .HasOne(e => e.Student)
                .WithMany(s => s.Enrollments)
                .HasForeignKey(e => e.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Enrollment>()
                .HasOne(e => e.Course)
                .WithMany(c => c.Enrollments)
                .HasForeignKey(e => e.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<CourseDocument>()
                .HasOne(d => d.Course)
                .WithMany(c => c.Documents)
                .HasForeignKey(d => d.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ChatMessage>()
                .HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ChatMessage>()
                .HasOne(m => m.Course)
                .WithMany()
                .HasForeignKey(m => m.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            // ==========================================
            // 2. 🎯 YENİ İLİŞKİLER (DEPARTMAN VE AI YÖNETİMİ)
            // ==========================================

            // Departman ve Kurs İlişkisi
            builder.Entity<Course>()
                .HasOne(c => c.Department)
                .WithMany(d => d.Courses)
                .HasForeignKey(c => c.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull); // Departman silinirse kurslar kalsın, id null olsun

            // Departman ve Kullanıcı İlişkisi
            builder.Entity<ApplicationUser>()
                .HasOne(u => u.Department)
                .WithMany(d => d.Users)
                .HasForeignKey(u => u.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);

            // Departman Yöneticisi İlişkisi
            builder.Entity<Department>()
                .HasOne(d => d.Manager)
                .WithMany()
                .HasForeignKey(d => d.ManagerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Kullanıcıların Kaydettiği AI API Anahtarları (UserAiProvider)
            builder.Entity<UserAiProvider>()
                .HasIndex(u => new { u.UserId, u.AiProviderId })
                .IsUnique(); // Bir öğrenci bir sağlayıcı (örn. ChatGPT) için sadece 1 anahtar kaydedebilir

            builder.Entity<UserAiProvider>()
                .HasOne(u => u.User)
                .WithMany(u => u.SavedAiKeys)
                .HasForeignKey(u => u.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserAiProvider>()
                .HasOne(u => u.AiProvider)
                .WithMany(p => p.UserAiProviders)
                .HasForeignKey(u => u.AiProviderId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}