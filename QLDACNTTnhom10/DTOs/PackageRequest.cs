namespace QLDACNTTnhom10.DTOs
{
    public class PackageRequest
    {
        public string PackageName { get; set; }
        public string PackageType { get; set; } // Gym, Yoga, PT
        public int DurationDays { get; set; }
        public int TotalSessions { get; set; }
        public decimal Price { get; set; }
        public bool IsFlexibleTime { get; set; } // 1: Tự do (Gym), 0: Cố định giờ (Yoga)
    }
}
