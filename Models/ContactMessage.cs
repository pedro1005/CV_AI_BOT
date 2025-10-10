namespace CvAssistantWeb.Models
{
    public class ContactMessage
    {
        public int Id { get; set; }
        public string Company { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        private DateTime _date;
        public DateTime Date
        {
            get => _date;
            set => _date = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        }
    }
}

