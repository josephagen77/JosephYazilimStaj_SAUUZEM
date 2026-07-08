using MiniLms.Interfaces;
using MiniLms.Models;
using MiniLms.ViewModels;
using AutoMapper;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MiniLms.Services
{
    public class StudentService : IStudentService
    {
        private readonly IStudentRepository _studentRepository;
        private readonly IMapper _mapper;

        public StudentService(IStudentRepository studentRepository, IMapper mapper)
        {
            _studentRepository = studentRepository;
            _mapper = mapper;
        }

        // Tüm öğrencileri repository'den çekip listeler
        public async Task<IEnumerable<Student>> GetAllStudentsAsync()
        {
            return await _studentRepository.GetAllAsync();
        }

        // ID değerine göre tek bir öğrenci getirir
        public async Task<Student?> GetStudentByIdAsync(int id)
        {
            return await _studentRepository.GetByIdAsync(id);
        }

        // Öğrenciyi aldığı ders kayıtlarıyla (Enrollments -> Course) birlikte getirir
        public async Task<Student?> GetStudentWithEnrollmentsAsync(int id)
        {
            return await _studentRepository.GetStudentWithEnrollmentsAsync(id);
        }

        public async Task<Student?> GetStudentByNumberAsync(string studentNumber)
        {
            return await _studentRepository.GetByStudentNumberAsync(studentNumber);
        }

        // Yeni öğrenci kaydeder ve mükerrer numara kontrolü yapar
        public async Task AddStudentAsync(StudentCreateViewModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            // İş Kuralı: Aynı öğrenci numarası sistemde var mı?
            var existingStudent = await _studentRepository.GetByStudentNumberAsync(model.StudentNumber);
            if (existingStudent != null)
            {
                throw new InvalidOperationException($"{model.StudentNumber} numaralı öğrenci zaten kayıtlı!");
            }

            // AutoMapper ile ViewModel -> Entity dönüşümü
            var studentEntity = _mapper.Map<Student>(model);

            await _studentRepository.AddAsync(studentEntity);
        }

        // Öğrenci bilgilerini günceller
        public async Task UpdateStudentAsync(StudentEditViewModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            var studentEntity = await _studentRepository.GetByIdAsync(model.Id);
            if (studentEntity == null)
            {
                throw new KeyNotFoundException("Güncellenmek istenen öğrenci bulunamadı.");
            }

            // İş Kuralı: Öğrenci numarası değiştirildiyse başkası kullanıyor mu?
            if (studentEntity.StudentNumber != model.StudentNumber)
            {
                var duplicateNumber = await _studentRepository.GetByStudentNumberAsync(model.StudentNumber);
                if (duplicateNumber != null)
                {
                    throw new InvalidOperationException($"{model.StudentNumber} numaralı öğrenci başka bir kayıtta kullanılıyor!");
                }
            }

            // ViewModel verilerini mevcut Entity üzerine yansıtıyoruz
            _mapper.Map(model, studentEntity);

            await _studentRepository.UpdateAsync(studentEntity);
        }

        // Öğrenciyi sistemden siler
        public async Task DeleteStudentAsync(int id)
        {
            var student = await _studentRepository.GetByIdAsync(id);
            if (student == null)
            {
                throw new KeyNotFoundException("Silinmek istenen öğrenci bulunamadı.");
            }

            await _studentRepository.DeleteAsync(id);
        }
    }
}
