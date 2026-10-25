namespace QLDACNTTnhom10.DTOs
{
    public class CreateContractRequest
    {
        public int MemberID { get; set; }
        public int PackageID { get; set; }
        public int? TrainerID { get; set; } // NULL nếu tập Gym tự do
        public int? ClassID { get; set; }   // NULL nếu không tập Yoga
        public decimal? DiscountAmount { get; set; }
        public string? PromoCode { get; set; }
        public string PaymentMethod { get; set; } // Tiền mặt, VNPAY, MoMo
    }
}
