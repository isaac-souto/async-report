﻿namespace ReportWorker.Models
{
    public class NotificationModel
    {
        public Guid UserId { get; set; }

        public string FileName { get; set; }

        public string Url { get; set; }
    }
}
