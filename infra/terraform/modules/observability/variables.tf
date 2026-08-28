variable "name" {
  description = "Prefix for alarm and topic names."
  type        = string
}

variable "alarm_email" {
  description = "Address subscribed to the alarm topic. Null leaves the topic without subscribers."
  type        = string
  default     = null
}

variable "queue_name" {
  description = "Name of the events queue."
  type        = string
}

variable "dlq_name" {
  description = "Name of the dead-letter queue."
  type        = string
}

variable "max_message_age_seconds" {
  description = "Age of the oldest message on the events queue that counts as the projections worker falling behind."
  type        = number
  default     = 300
}

variable "db_instance_identifier" {
  description = "RDS instance identifier to alarm on."
  type        = string
}

variable "db_free_storage_threshold_bytes" {
  description = "Free storage below which the database alarm fires."
  type        = number
  default     = 2147483648
}

variable "cluster_name" {
  description = "EKS cluster the CloudWatch observability add-on is installed into."
  type        = string
}

variable "oidc_provider_arn" {
  description = "IAM OIDC provider of the cluster, for the agent's IRSA role."
  type        = string
}

variable "oidc_issuer" {
  description = "OIDC issuer without the scheme."
  type        = string
}

variable "log_retention_days" {
  description = "Retention for the Container Insights log groups."
  type        = number
}
