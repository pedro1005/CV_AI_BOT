namespace CvAssistantWeb.Models
{
    public class ContactMessage
    {
        public int Id { get; set; }
        public string Company { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Date { get; set; }
    }
}

