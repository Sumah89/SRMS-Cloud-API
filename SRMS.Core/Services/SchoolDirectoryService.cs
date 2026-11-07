using System;
using System.Collections.Generic;
using SRMS.Desktop.Data;
using SRMS.Desktop.Models;

namespace SRMS.Desktop.Services
{
    public sealed class SchoolDirectoryService
    {
        private readonly DatabaseHelper _database;

        public SchoolDirectoryService(DatabaseHelper database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public IReadOnlyList<School> GetAll()
        {
            return _database.GetAllSchools();
        }

        public School GetById(int schoolId)
        {
            return schoolId > 0 ? _database.GetSchoolById(schoolId) : null;
        }
    }
}
