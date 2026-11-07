using System;

namespace SRMS.Desktop.Models
{
    public class Student
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public int ClassId { get; set; }
        public string AdmissionNumber { get; set; }
        public string FullName { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string Gender { get; set; }
        public string Address { get; set; }
        public string ParentName { get; set; }
        public string ParentPhone { get; set; }
        public string HouseColour { get; set; }
        public string Stream { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }

        // Student photo path property
        public string PhotoPath { get; set; }

        // Helper property for display (not stored in database)
        public string ClassName { get; set; }
    }
}