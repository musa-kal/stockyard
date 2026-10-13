output "queue_url" {
  description = "URL of the events queue (Messaging__QueueUrl)."
  value       = aws_sqs_queue.events.url
}

output "queue_arn" {
  description = "ARN of the events queue."
  value       = aws_sqs_queue.events.arn
}

output "queue_name" {
  description = "Name of the events queue."
  value       = aws_sqs_queue.events.name
}

output "dlq_url" {
  description = "URL of the dead-letter queue (Messaging__DlqUrl)."
  value       = aws_sqs_queue.dlq.url
}

output "dlq_arn" {
  description = "ARN of the dead-letter queue."
  value       = aws_sqs_queue.dlq.arn
}

output "dlq_name" {
  description = "Name of the dead-letter queue."
  value       = aws_sqs_queue.dlq.name
}
