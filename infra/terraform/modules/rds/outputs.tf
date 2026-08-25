output "endpoint" {
  description = "Hostname of the instance."
  value       = aws_db_instance.this.address
}

output "port" {
  description = "Port SQL Server listens on."
  value       = aws_db_instance.this.port
}

output "master_username" {
  description = "Master login name."
  value       = aws_db_instance.this.username
}

output "master_user_secret_arn" {
  description = "ARN of the RDS managed secret holding the master password."
  value       = aws_db_instance.this.master_user_secret[0].secret_arn
}

output "security_group_id" {
  description = "Security group attached to the instance."
  value       = aws_security_group.this.id
}
