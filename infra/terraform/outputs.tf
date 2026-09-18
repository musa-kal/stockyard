output "cluster_name" {
  description = "EKS cluster name, for aws eks update-kubeconfig."
  value       = module.eks.cluster_name
}

output "queue_url" {
  description = "Messaging__QueueUrl."
  value       = module.messaging.queue_url
}

output "dlq_url" {
  description = "Messaging__DlqUrl."
  value       = module.messaging.dlq_url
}

output "export_bucket" {
  description = "Bucket the Exporter writes to and dbt reads from."
  value       = module.storage.bucket_name
}

output "export_destination" {
  description = "Exporter__Destination."
  value       = module.storage.destination
}

output "ecr_repository_urls" {
  description = "ECR repository URL per image."
  value       = module.registry.repository_urls
}

output "service_role_arns" {
  description = "IRSA role ARN per Kubernetes service account, for the eks.amazonaws.com/role-arn annotations."
  value       = module.eks.service_role_arns
}

output "db_endpoint" {
  description = "SQL Server hostname, used when writing the connection string secret."
  value       = module.rds.endpoint
}

output "db_master_user_secret_arn" {
  description = "Secret RDS manages for the master password."
  value       = module.rds.master_user_secret_arn
}

output "db_secret_name" {
  description = "Secrets Manager name holding ConnectionStrings__Stockyard."
  value       = aws_secretsmanager_secret.db.name
}

output "signing_secret_name" {
  description = "Secrets Manager name holding Notifier__SigningSecret."
  value       = aws_secretsmanager_secret.signing.name
}

output "alarm_topic_arn" {
  description = "SNS topic the CloudWatch alarms publish to."
  value       = module.observability.alarm_topic_arn
}

output "k8s_aws_env" {
  description = "Contents for deploy/k8s/overlays/aws/aws.env: terraform output -raw k8s_aws_env > deploy/k8s/overlays/aws/aws.env"
  value       = <<-EOT
    Messaging__QueueUrl=${module.messaging.queue_url}
    Messaging__DlqUrl=${module.messaging.dlq_url}
    Messaging__ServiceUrl=
    Exporter__Destination=${module.storage.destination}
    Exporter__ServiceUrl=
  EOT
}

output "account_id" {
  description = "AWS account ID, substituted for the placeholder in the aws overlay."
  value       = data.aws_caller_identity.current.account_id
}
