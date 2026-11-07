using System;

namespace SRMS.Desktop.Models
{
    /// </summary>
    public class PromotionPath
    {
        public int Id { get; set; }

        /// </summary>
        public int SchoolId { get; set; }

        /// <summary>Base class name (e.g. "JSS I", "SSS III"). Stream ignored.</summary>
        public string FromClassName { get; set; }

        /// </summary>
        public string ToClassName { get; set; }

        public bool IsTerminal { get; set; }

        /// <summary>Sort order for display in configuration UIs.</summary>
        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
    }
}