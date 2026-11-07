using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using SRMS.Desktop.Models;

namespace SRMS.Desktop.Data.Repositories
{
    public interface IRepository<T> where T : class
    {
        Task<T> GetByIdAsync(int id);
        Task<IEnumerable<T>> GetAllAsync();
        Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate);
        Task<int> InsertAsync(T entity);
        Task<bool> UpdateAsync(T entity);
        Task<bool> DeleteAsync(int id);
        Task<bool> SoftDeleteAsync(int id, string deletedBy);
        Task<int> CountAsync();
        Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate);
    }

    public abstract class BaseRepository<T> : IRepository<T> where T : class
    {
        protected readonly DatabaseHelper _dbHelper;
        protected readonly string _tableName;

        protected BaseRepository(string tableName)
        {
            _dbHelper = new DatabaseHelper();
            _tableName = tableName;
        }

        public abstract Task<T> GetByIdAsync(int id);
        public abstract Task<IEnumerable<T>> GetAllAsync();
        public abstract Task<int> InsertAsync(T entity);
        public abstract Task<bool> UpdateAsync(T entity);

        public virtual async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
        {
            var all = await GetAllAsync();
            return all.Where(predicate.Compile());
        }

        public virtual async Task<bool> DeleteAsync(int id)
        {
            var result = await Task.Run(() => _dbHelper.ExecuteNonQuery($"DELETE FROM {_tableName} WHERE Id = @Id",
                new Dictionary<string, object> { { "@Id", id } }) > 0);
            return result;
        }

        public virtual async Task<bool> SoftDeleteAsync(int id, string deletedBy)
        {
            var parameters = new Dictionary<string, object>
            {
                { "@Id", id },
                { "@DeletedDate", DateTime.Now },
                { "@DeletedBy", deletedBy ?? Global.CurrentUser?.Username ?? "System" }
            };

            var result = await Task.Run(() => _dbHelper.ExecuteNonQuery(
                $"UPDATE {_tableName} SET IsDeleted = 1, DeletedDate = @DeletedDate, DeletedBy = @DeletedBy WHERE Id = @Id",
                parameters) > 0);

            return result;
        }

        public virtual async Task<int> CountAsync()
        {
            var result = await Task.Run(() => _dbHelper.ExecuteScalar($"SELECT COUNT(*) FROM {_tableName} WHERE (IsDeleted = 0 OR IsDeleted IS NULL)"));
            return Convert.ToInt32(result);
        }

        public virtual async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate)
        {
            var items = await FindAsync(predicate);
            return items.Any();
        }

        protected DataTable ExecuteQuery(string query, Dictionary<string, object> parameters = null)
        {
            return _dbHelper.ExecuteQuery(query, parameters);
        }

        protected int ExecuteNonQuery(string query, Dictionary<string, object> parameters = null)
        {
            return _dbHelper.ExecuteNonQuery(query, parameters);
        }

        protected object ExecuteScalar(string query, Dictionary<string, object> parameters = null)
        {
            return _dbHelper.ExecuteScalar(query, parameters);
        }
    }

    // Student Repository Implementation
    public class StudentRepository : BaseRepository<Student>
    {
        public StudentRepository() : base("Students") { }

        public override async Task<Student> GetByIdAsync(int id)
        {
            var dt = await Task.Run(() => ExecuteQuery("SELECT * FROM Students WHERE Id = @Id AND (IsDeleted = 0 OR IsDeleted IS NULL)",
                new Dictionary<string, object> { { "@Id", id } }));

            if (dt.Rows.Count == 0) return null;

            var row = dt.Rows[0];
            return new Student
            {
                Id = Convert.ToInt32(row["Id"]),
                SchoolId = Convert.ToInt32(row["SchoolId"]),
                ClassId = Convert.ToInt32(row["ClassId"]),
                AdmissionNumber = row["AdmissionNumber"]?.ToString(),
                FullName = row["FullName"]?.ToString(),
                DateOfBirth = Convert.ToDateTime(row["DateOfBirth"]),
                Gender = row["Gender"]?.ToString(),
                Address = row["Address"]?.ToString(),
                ParentName = row["ParentName"]?.ToString(),
                ParentPhone = row["ParentPhone"]?.ToString(),
                HouseColour = row["HouseColour"]?.ToString(),
                IsActive = Convert.ToBoolean(row["IsActive"]),
                CreatedDate = Convert.ToDateTime(row["CreatedDate"]),
                ModifiedDate = Convert.ToDateTime(row["ModifiedDate"])
            };
        }

        public override async Task<IEnumerable<Student>> GetAllAsync()
        {
            var students = new List<Student>();
            var dt = await Task.Run(() => ExecuteQuery("SELECT * FROM Students WHERE (IsDeleted = 0 OR IsDeleted IS NULL) ORDER BY FullName"));

            foreach (DataRow row in dt.Rows)
            {
                students.Add(new Student
                {
                    Id = Convert.ToInt32(row["Id"]),
                    SchoolId = Convert.ToInt32(row["SchoolId"]),
                    ClassId = Convert.ToInt32(row["ClassId"]),
                    AdmissionNumber = row["AdmissionNumber"]?.ToString(),
                    FullName = row["FullName"]?.ToString(),
                    DateOfBirth = Convert.ToDateTime(row["DateOfBirth"]),
                    Gender = row["Gender"]?.ToString(),
                    Address = row["Address"]?.ToString(),
                    ParentName = row["ParentName"]?.ToString(),
                    ParentPhone = row["ParentPhone"]?.ToString(),
                    HouseColour = row["HouseColour"]?.ToString(),
                    IsActive = Convert.ToBoolean(row["IsActive"]),
                    CreatedDate = Convert.ToDateTime(row["CreatedDate"]),
                    ModifiedDate = Convert.ToDateTime(row["ModifiedDate"])
                });
            }

            return students;
        }

        public override async Task<int> InsertAsync(Student entity)
        {
            var query = @"INSERT INTO Students 
                (SchoolId, ClassId, AdmissionNumber, FullName, DateOfBirth, Gender, 
                 Address, ParentName, ParentPhone, HouseColour, IsActive, CreatedDate, ModifiedDate)
                VALUES 
                (@SchoolId, @ClassId, @AdmissionNumber, @FullName, @DateOfBirth, @Gender,
                 @Address, @ParentName, @ParentPhone, @HouseColour, @IsActive, @CreatedDate, @ModifiedDate);
                SELECT last_insert_rowid();";

            var parameters = new Dictionary<string, object>
            {
                { "@SchoolId", entity.SchoolId },
                { "@ClassId", entity.ClassId },
                { "@AdmissionNumber", entity.AdmissionNumber },
                { "@FullName", entity.FullName },
                { "@DateOfBirth", entity.DateOfBirth },
                { "@Gender", entity.Gender },
                { "@Address", entity.Address ?? "" },
                { "@ParentName", entity.ParentName ?? "" },
                { "@ParentPhone", entity.ParentPhone ?? "" },
                { "@HouseColour", entity.HouseColour ?? "" },
                { "@IsActive", entity.IsActive },
                { "@CreatedDate", DateTime.Now },
                { "@ModifiedDate", DateTime.Now }
            };

            var id = await Task.Run(() => Convert.ToInt32(ExecuteScalar(query, parameters)));
            return id;
        }

        public override async Task<bool> UpdateAsync(Student entity)
        {
            var query = @"UPDATE Students SET
                ClassId = @ClassId, FullName = @FullName, DateOfBirth = @DateOfBirth,
                Gender = @Gender, Address = @Address, ParentName = @ParentName,
                ParentPhone = @ParentPhone, HouseColour = @HouseColour, IsActive = @IsActive, ModifiedDate = @ModifiedDate
                WHERE Id = @Id";

            var parameters = new Dictionary<string, object>
            {
                { "@Id", entity.Id },
                { "@ClassId", entity.ClassId },
                { "@FullName", entity.FullName },
                { "@DateOfBirth", entity.DateOfBirth },
                { "@Gender", entity.Gender },
                { "@Address", entity.Address ?? "" },
                { "@ParentName", entity.ParentName ?? "" },
                { "@ParentPhone", entity.ParentPhone ?? "" },
                { "@HouseColour", entity.HouseColour ?? "" },
                { "@IsActive", entity.IsActive },
                { "@ModifiedDate", DateTime.Now }
            };

            var result = await Task.Run(() => ExecuteNonQuery(query, parameters) > 0);
            return result;
        }

        // Additional methods specific to Student
        public async Task<IEnumerable<Student>> GetStudentsByClassAsync(int classId)
        {
            var students = new List<Student>();
            var dt = await Task.Run(() => ExecuteQuery(
                "SELECT * FROM Students WHERE ClassId = @ClassId AND (IsDeleted = 0 OR IsDeleted IS NULL) ORDER BY FullName",
                new Dictionary<string, object> { { "@ClassId", classId } }));

            foreach (DataRow row in dt.Rows)
            {
                students.Add(new Student
                {
                    Id = Convert.ToInt32(row["Id"]),
                    SchoolId = Convert.ToInt32(row["SchoolId"]),
                    ClassId = Convert.ToInt32(row["ClassId"]),
                    AdmissionNumber = row["AdmissionNumber"]?.ToString(),
                    FullName = row["FullName"]?.ToString(),
                    DateOfBirth = Convert.ToDateTime(row["DateOfBirth"]),
                    Gender = row["Gender"]?.ToString(),
                    Address = row["Address"]?.ToString(),
                    ParentName = row["ParentName"]?.ToString(),
                    ParentPhone = row["ParentPhone"]?.ToString(),
                    HouseColour = row["HouseColour"]?.ToString(),
                    IsActive = Convert.ToBoolean(row["IsActive"]),
                    CreatedDate = Convert.ToDateTime(row["CreatedDate"]),
                    ModifiedDate = Convert.ToDateTime(row["ModifiedDate"])
                });
            }

            return students;
        }

        public async Task<Student> GetStudentByAdmissionNumberAsync(string admissionNumber)
        {
            var dt = await Task.Run(() => ExecuteQuery(
                "SELECT * FROM Students WHERE AdmissionNumber = @AdmissionNumber AND (IsDeleted = 0 OR IsDeleted IS NULL)",
                new Dictionary<string, object> { { "@AdmissionNumber", admissionNumber } }));

            if (dt.Rows.Count == 0) return null;

            var row = dt.Rows[0];
            return new Student
            {
                Id = Convert.ToInt32(row["Id"]),
                SchoolId = Convert.ToInt32(row["SchoolId"]),
                ClassId = Convert.ToInt32(row["ClassId"]),
                AdmissionNumber = row["AdmissionNumber"]?.ToString(),
                FullName = row["FullName"]?.ToString(),
                DateOfBirth = Convert.ToDateTime(row["DateOfBirth"]),
                Gender = row["Gender"]?.ToString(),
                Address = row["Address"]?.ToString(),
                ParentName = row["ParentName"]?.ToString(),
                ParentPhone = row["ParentPhone"]?.ToString(),
                HouseColour = row["HouseColour"]?.ToString(),
                IsActive = Convert.ToBoolean(row["IsActive"]),
                CreatedDate = Convert.ToDateTime(row["CreatedDate"]),
                ModifiedDate = Convert.ToDateTime(row["ModifiedDate"])
            };
        }
    }

    // Class Repository Implementation
    public class ClassRepository : BaseRepository<Class>
    {
        public ClassRepository() : base("Classes") { }

        public override async Task<Class> GetByIdAsync(int id)
        {
            var dt = await Task.Run(() => ExecuteQuery("SELECT * FROM Classes WHERE Id = @Id",
                new Dictionary<string, object> { { "@Id", id } }));

            if (dt.Rows.Count == 0) return null;

            var row = dt.Rows[0];
            return new Class
            {
                Id = Convert.ToInt32(row["Id"]),
                SchoolId = Convert.ToInt32(row["SchoolId"]),
                ClassName = row["ClassName"]?.ToString(),
                ClassLevel = row["ClassLevel"]?.ToString(),
                Stream = row["Stream"]?.ToString(),
                AcademicYear = row["AcademicYear"]?.ToString(),
                ClassTeacherName = row["ClassTeacherName"]?.ToString(),
                IsActive = Convert.ToBoolean(row["IsActive"]),
                CreatedDate = Convert.ToDateTime(row["CreatedDate"]),
                ModifiedDate = Convert.ToDateTime(row["ModifiedDate"])
            };
        }

        public override async Task<IEnumerable<Class>> GetAllAsync()
        {
            var classes = new List<Class>();
            var dt = await Task.Run(() => ExecuteQuery("SELECT * FROM Classes ORDER BY ClassLevel, Stream"));

            foreach (DataRow row in dt.Rows)
            {
                classes.Add(new Class
                {
                    Id = Convert.ToInt32(row["Id"]),
                    SchoolId = Convert.ToInt32(row["SchoolId"]),
                    ClassName = row["ClassName"]?.ToString(),
                    ClassLevel = row["ClassLevel"]?.ToString(),
                    Stream = row["Stream"]?.ToString(),
                    AcademicYear = row["AcademicYear"]?.ToString(),
                    ClassTeacherName = row["ClassTeacherName"]?.ToString(),
                    IsActive = Convert.ToBoolean(row["IsActive"]),
                    CreatedDate = Convert.ToDateTime(row["CreatedDate"]),
                    ModifiedDate = Convert.ToDateTime(row["ModifiedDate"])
                });
            }

            return classes;
        }

        public override async Task<int> InsertAsync(Class entity)
        {
            var query = @"INSERT INTO Classes 
                (SchoolId, ClassName, ClassLevel, Stream, AcademicYear, ClassTeacherName, IsActive, CreatedDate, ModifiedDate)
                VALUES 
                (@SchoolId, @ClassName, @ClassLevel, @Stream, @AcademicYear, @ClassTeacherName, @IsActive, @CreatedDate, @ModifiedDate);
                SELECT last_insert_rowid();";

            var parameters = new Dictionary<string, object>
            {
                { "@SchoolId", entity.SchoolId },
                { "@ClassName", entity.ClassName },
                { "@ClassLevel", entity.ClassLevel },
                { "@Stream", entity.Stream ?? "" },
                { "@AcademicYear", entity.AcademicYear },
                { "@ClassTeacherName", entity.ClassTeacherName ?? "" },
                { "@IsActive", entity.IsActive },
                { "@CreatedDate", DateTime.Now },
                { "@ModifiedDate", DateTime.Now }
            };

            var id = await Task.Run(() => Convert.ToInt32(ExecuteScalar(query, parameters)));
            return id;
        }

        public override async Task<bool> UpdateAsync(Class entity)
        {
            var query = @"UPDATE Classes SET
                ClassName = @ClassName, ClassLevel = @ClassLevel, Stream = @Stream,
                AcademicYear = @AcademicYear, ClassTeacherName = @ClassTeacherName, IsActive = @IsActive, ModifiedDate = @ModifiedDate
                WHERE Id = @Id";

            var parameters = new Dictionary<string, object>
            {
                { "@Id", entity.Id },
                { "@ClassName", entity.ClassName },
                { "@ClassLevel", entity.ClassLevel },
                { "@Stream", entity.Stream ?? "" },
                { "@AcademicYear", entity.AcademicYear },
                { "@ClassTeacherName", entity.ClassTeacherName ?? "" },
                { "@IsActive", entity.IsActive },
                { "@ModifiedDate", DateTime.Now }
            };

            var result = await Task.Run(() => ExecuteNonQuery(query, parameters) > 0);
            return result;
        }

        public async Task<IEnumerable<Class>> GetClassesBySchoolAsync(int schoolId)
        {
            var classes = new List<Class>();
            var dt = await Task.Run(() => ExecuteQuery(
                "SELECT * FROM Classes WHERE SchoolId = @SchoolId AND IsActive = 1 ORDER BY ClassLevel, Stream",
                new Dictionary<string, object> { { "@SchoolId", schoolId } }));

            foreach (DataRow row in dt.Rows)
            {
                classes.Add(new Class
                {
                    Id = Convert.ToInt32(row["Id"]),
                    SchoolId = Convert.ToInt32(row["SchoolId"]),
                    ClassName = row["ClassName"]?.ToString(),
                    ClassLevel = row["ClassLevel"]?.ToString(),
                    Stream = row["Stream"]?.ToString(),
                    AcademicYear = row["AcademicYear"]?.ToString(),
                    ClassTeacherName = row["ClassTeacherName"]?.ToString(),
                    IsActive = Convert.ToBoolean(row["IsActive"]),
                    CreatedDate = Convert.ToDateTime(row["CreatedDate"]),
                    ModifiedDate = Convert.ToDateTime(row["ModifiedDate"])
                });
            }

            return classes;
        }
    }

    // Subject Repository Implementation
    public class SubjectRepository : BaseRepository<Subject>
    {
        public SubjectRepository() : base("Subjects") { }

        public override async Task<Subject> GetByIdAsync(int id)
        {
            var dt = await Task.Run(() => ExecuteQuery("SELECT * FROM Subjects WHERE Id = @Id",
                new Dictionary<string, object> { { "@Id", id } }));

            if (dt.Rows.Count == 0) return null;

            var row = dt.Rows[0];
                return new Subject
            {
                Id = Convert.ToInt32(row["Id"]),
                SchoolId = Convert.ToInt32(row["SchoolId"]),
                SubjectCode = row["SubjectCode"]?.ToString(),
                SubjectName = row["SubjectName"]?.ToString(),
                    AcademicYear = row.Table.Columns.Contains("AcademicYear") ? Convert.ToString(row["AcademicYear"]) : null,
                IsCore = Convert.ToBoolean(row["IsCore"]),
                ClassId = row["ClassId"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["ClassId"]),
                PassMark = Convert.ToDecimal(row["PassMark"]),
                IsActive = Convert.ToBoolean(row["IsActive"]),
                CreatedDate = Convert.ToDateTime(row["CreatedDate"]),
                ModifiedDate = Convert.ToDateTime(row["ModifiedDate"])
            };
        }

        public override async Task<IEnumerable<Subject>> GetAllAsync()
        {
            var subjects = new List<Subject>();
            var dt = await Task.Run(() => ExecuteQuery("SELECT * FROM Subjects ORDER BY SubjectName"));

            foreach (DataRow row in dt.Rows)
            {
                subjects.Add(new Subject
                {
                    Id = Convert.ToInt32(row["Id"]),
                    SchoolId = Convert.ToInt32(row["SchoolId"]),
                    SubjectCode = row["SubjectCode"]?.ToString(),
                    SubjectName = row["SubjectName"]?.ToString(),
                    AcademicYear = row.Table.Columns.Contains("AcademicYear") ? Convert.ToString(row["AcademicYear"]) : null,
                    IsCore = Convert.ToBoolean(row["IsCore"]),
                    ClassId = row["ClassId"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["ClassId"]),
                    PassMark = Convert.ToDecimal(row["PassMark"]),
                    IsActive = Convert.ToBoolean(row["IsActive"]),
                    CreatedDate = Convert.ToDateTime(row["CreatedDate"]),
                    ModifiedDate = Convert.ToDateTime(row["ModifiedDate"])
                });
            }

            return subjects;
        }

        public override async Task<int> InsertAsync(Subject entity)
        {
            var query = @"INSERT INTO Subjects 
                (SchoolId, SubjectCode, SubjectName, IsCore, ClassId, PassMark, IsActive, CreatedDate, ModifiedDate)
                VALUES 
                (@SchoolId, @SubjectCode, @SubjectName, @IsCore, @ClassId, @PassMark, @IsActive, @CreatedDate, @ModifiedDate);
                SELECT last_insert_rowid();";

            var parameters = new Dictionary<string, object>
            {
                { "@SchoolId", entity.SchoolId },
                { "@SubjectCode", entity.SubjectCode ?? "" },
                { "@SubjectName", entity.SubjectName },
                { "@IsCore", entity.IsCore },
                { "@ClassId", entity.ClassId },
                { "@PassMark", entity.PassMark },
                { "@IsActive", entity.IsActive },
                { "@CreatedDate", DateTime.Now },
                { "@ModifiedDate", DateTime.Now }
            };

            var id = await Task.Run(() => Convert.ToInt32(ExecuteScalar(query, parameters)));
            return id;
        }

        public override async Task<bool> UpdateAsync(Subject entity)
        {
            var query = @"UPDATE Subjects SET
                SubjectCode = @SubjectCode, SubjectName = @SubjectName, IsCore = @IsCore,
                ClassId = @ClassId, PassMark = @PassMark, IsActive = @IsActive, ModifiedDate = @ModifiedDate
                WHERE Id = @Id";

            var parameters = new Dictionary<string, object>
            {
                { "@Id", entity.Id },
                { "@SubjectCode", entity.SubjectCode ?? "" },
                { "@SubjectName", entity.SubjectName },
                { "@IsCore", entity.IsCore },
                { "@ClassId", entity.ClassId },
                { "@PassMark", entity.PassMark },
                { "@IsActive", entity.IsActive },
                { "@ModifiedDate", DateTime.Now }
            };

            var result = await Task.Run(() => ExecuteNonQuery(query, parameters) > 0);
            return result;
        }

        public async Task<IEnumerable<Subject>> GetSubjectsBySchoolAsync(int schoolId)
        {
            var subjects = new List<Subject>();
            var dt = await Task.Run(() => ExecuteQuery(
                "SELECT * FROM Subjects WHERE SchoolId = @SchoolId AND IsActive = 1 ORDER BY SubjectName",
                new Dictionary<string, object> { { "@SchoolId", schoolId } }));

            foreach (DataRow row in dt.Rows)
            {
                subjects.Add(new Subject
                {
                    Id = Convert.ToInt32(row["Id"]),
                    SchoolId = Convert.ToInt32(row["SchoolId"]),
                    SubjectCode = row["SubjectCode"]?.ToString(),
                    SubjectName = row["SubjectName"]?.ToString(),
                    IsCore = Convert.ToBoolean(row["IsCore"]),
                    ClassId = row["ClassId"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["ClassId"]),
                    PassMark = Convert.ToDecimal(row["PassMark"]),
                    IsActive = Convert.ToBoolean(row["IsActive"]),
                    CreatedDate = Convert.ToDateTime(row["CreatedDate"]),
                    ModifiedDate = Convert.ToDateTime(row["ModifiedDate"])
                });
            }

            return subjects;
        }

        public async Task<IEnumerable<Subject>> GetSubjectsByClassAsync(int classId)
        {
            var subjects = new List<Subject>();
            var dt = await Task.Run(() => ExecuteQuery(
                "SELECT * FROM Subjects WHERE (ClassId = @ClassId OR ClassId IS NULL) AND IsActive = 1 ORDER BY SubjectName",
                new Dictionary<string, object> { { "@ClassId", classId } }));

            foreach (DataRow row in dt.Rows)
            {
                subjects.Add(new Subject
                {
                    Id = Convert.ToInt32(row["Id"]),
                    SchoolId = Convert.ToInt32(row["SchoolId"]),
                    SubjectCode = row["SubjectCode"]?.ToString(),
                    SubjectName = row["SubjectName"]?.ToString(),
                    IsCore = Convert.ToBoolean(row["IsCore"]),
                    ClassId = row["ClassId"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["ClassId"]),
                    PassMark = Convert.ToDecimal(row["PassMark"]),
                    IsActive = Convert.ToBoolean(row["IsActive"]),
                    CreatedDate = Convert.ToDateTime(row["CreatedDate"]),
                    ModifiedDate = Convert.ToDateTime(row["ModifiedDate"])
                });
            }

            return subjects;
        }
    }
}