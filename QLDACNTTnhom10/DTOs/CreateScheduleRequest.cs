namespace QLDACNTTnhom10.DTOs
{
    public class CreateScheduleRequest
    {
        public int ClassID { get; set; }
        public string Room { get; set; } // Gán phòng tập
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }
}
