namespace QLDACNTTnhom10.DTOs
{
    public class CreateClassRequest
    {
        public string ClassName { get; set; }
        public int TrainerID { get; set; } // Phân công PT cho lớp
        public int Capacity { get; set; }
    }
}
