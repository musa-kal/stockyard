output "bucket_name" {
  description = "Name of the export bucket."
  value       = aws_s3_bucket.exports.bucket
}

output "bucket_arn" {
  description = "ARN of the export bucket."
  value       = aws_s3_bucket.exports.arn
}

output "export_prefix" {
  description = "Key prefix the Exporter writes under."
  value       = var.export_prefix
}

output "destination" {
  description = "Value for Exporter__Destination."
  value       = "s3://${aws_s3_bucket.exports.bucket}/${var.export_prefix}"
}
