using System;
using System.Collections.Generic;

namespace SRMS.Desktop.Sync.Grants
{
    /// <summary>Result of an admin login check.</summary>
    public sealed class AdminLoginResult
    {
        public bool   Success      { get; set; }
        public int    UserId       { get; set; }
        public string Username     { get; set; } = "";
        public string FullName     { get; set; } = "";
        public int    SchoolId     { get; set; }
        public string SchoolName   { get; set; } = "";
    }

    /// <summary>Result of a teacher login check.</summary>
    public sealed class TeacherCodeLookupResult
    {
        public bool   Success      { get; set; }
        public int    UserId       { get; set; }
        public int    SchoolId     { get; set; }
        public string FullName     { get; set; } = "";
        public string Username     { get; set; } = "";
        public string SchoolName   { get; set; } = "";
        public string PhoneE164    { get; set; } = "";
        public string Code         { get; set; } = "";
    }

    /// <summary>A single child linked to a parent.</summary>
    public sealed class LinkedChild
    {
        public int    StudentId        { get; set; }
        public int    SchoolId         { get; set; }
        public string StudentName      { get; set; } = "";
        public string AdmissionNumber  { get; set; } = "";
        public string ClassName        { get; set; } = "";
        public string SchoolName       { get; set; } = "";
    }

    /// <summary>Result of a parent login check.</summary>
    public sealed class ParentCodeLookupResult
    {
        public bool                 Success      { get; set; }
        public int                  UserId       { get; set; }
        public string               FullName     { get; set; } = "";
        public List<LinkedChild>    Children     { get; set; } = new List<LinkedChild>();
    }

    /// <summary>Model used by the Desktop's EditTeacherCodeDialog.</summary>
    public sealed class TeacherCodeRow
    {
        public int    Id            { get; set; }
        public int    UserId        { get; set; }
        public int    SchoolId      { get; set; }
        public string PhoneE164     { get; set; } = "";
        public string Code          { get; set; } = "";
        public bool   IsActive      { get; set; }
        public long   CreatedAt     { get; set; }
        public string UserFullName  { get; set; } = "";
        public string UserRole      { get; set; } = "";
    }

    /// <summary>Model used by the Desktop's EditParentCodeDialog.</summary>
    public sealed class ParentCodeRow
    {
        public int    Id               { get; set; }
        public int    ParentUserId     { get; set; }
        public int    SchoolId         { get; set; }
        public int    StudentId        { get; set; }
        public string PhoneE164        { get; set; } = "";
        public string Code             { get; set; } = "";
        public bool   IsActive         { get; set; }
        public long   CreatedAt        { get; set; }
        public string ParentName       { get; set; } = "";
        public string ParentPhone      { get; set; } = "";
        public string StudentName      { get; set; } = "";
        public string AdmissionNumber  { get; set; } = "";
        public string ClassName        { get; set; } = "";
    }
}