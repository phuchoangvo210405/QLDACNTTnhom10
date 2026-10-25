namespace QLDACNTTnhom10.DTOs
{
    public class PackageDto
    {
        public int PackageID { get; set; }
        public string PackageName { get; set; }
        public string PackageType { get; set; }
        public int DurationDays { get; set; }
        public int TotalSessions { get; set; }
        public decimal Price { get; set; }
        public bool IsFlexibleTime { get; set; }
        public bool IsActive { get; set; }
    }
}
