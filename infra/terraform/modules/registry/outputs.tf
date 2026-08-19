output "repository_urls" {
  description = "Repository URL per image name."
  value       = { for name, repo in aws_ecr_repository.this : name => repo.repository_url }
}

output "repository_arns" {
  description = "Repository ARN per image name."
  value       = { for name, repo in aws_ecr_repository.this : name => repo.arn }
}

output "registry_id" {
  description = "Registry (account) ID the repositories live in."
  value       = one(distinct([for repo in aws_ecr_repository.this : repo.registry_id]))
}
