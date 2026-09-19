namespace QLDACNTTnhom10.DTOs
{
    public class MemberResponseDto
    {
        public int MemberID { get; set; }
        public int UserID { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string Gender { get; set; }
        public bool IsActive { get; set; }
    }
}
