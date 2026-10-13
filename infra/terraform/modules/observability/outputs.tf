output "alarm_topic_arn" {
  description = "SNS topic every alarm notifies."
  value       = aws_sns_topic.alarms.arn
}
