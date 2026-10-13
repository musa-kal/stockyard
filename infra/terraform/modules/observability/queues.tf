# Anything in the DLQ means a message failed five receives. The api can replay
# it (POST /api/v1/outbox/dlq/replay) once the cause is fixed.
resource "aws_cloudwatch_metric_alarm" "dlq_not_empty" {
  alarm_name          = "${var.name}-dlq-not-empty"
  alarm_description   = "Messages are sitting in ${var.dlq_name}. See docs/runbook.md, replay the DLQ."
  namespace           = "AWS/SQS"
  metric_name         = "ApproximateNumberOfMessagesVisible"
  dimensions          = { QueueName = var.dlq_name }
  statistic           = "Maximum"
  period              = 300
  evaluation_periods  = 1
  comparison_operator = "GreaterThanThreshold"
  threshold           = 0
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.alarms.arn]
  ok_actions          = [aws_sns_topic.alarms.arn]
}

# The projections worker is the only consumer; an old message means it is down
# or stuck and stock on hand is going stale.
resource "aws_cloudwatch_metric_alarm" "events_backlog" {
  alarm_name          = "${var.name}-events-backlog"
  alarm_description   = "The oldest message on ${var.queue_name} is older than ${var.max_message_age_seconds} seconds. Check the projections deployment."
  namespace           = "AWS/SQS"
  metric_name         = "ApproximateAgeOfOldestMessage"
  dimensions          = { QueueName = var.queue_name }
  statistic           = "Maximum"
  period              = 300
  evaluation_periods  = 2
  comparison_operator = "GreaterThanThreshold"
  threshold           = var.max_message_age_seconds
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.alarms.arn]
  ok_actions          = [aws_sns_topic.alarms.arn]
}
