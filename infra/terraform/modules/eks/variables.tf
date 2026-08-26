variable "cluster_name" {
  description = "Name of the EKS cluster."
  type        = string
}

variable "cluster_version" {
  description = "Kubernetes minor version for the control plane and node group."
  type        = string
}

variable "subnet_ids" {
  description = "Private subnets for the control plane network interfaces and the nodes."
  type        = list(string)
}

variable "endpoint_public_access_cidrs" {
  description = "CIDR blocks allowed to reach the public API server endpoint."
  type        = list(string)
}

variable "node_instance_types" {
  description = "Instance types for the managed node group."
  type        = list(string)
}

variable "node_min_size" {
  description = "Minimum nodes in the managed node group."
  type        = number
}

variable "node_desired_size" {
  description = "Nodes the group starts with. Ignored after creation so the cluster autoscaler or a manual scale is not undone."
  type        = number
}

variable "node_max_size" {
  description = "Maximum nodes in the managed node group."
  type        = number
}

variable "node_disk_size" {
  description = "Root volume size in GiB for each node."
  type        = number
  default     = 30
}

variable "namespace" {
  description = "Kubernetes namespace the Stockyard service accounts live in."
  type        = string
  default     = "stockyard"
}

variable "queue_arn" {
  description = "ARN of the stockyard-events queue."
  type        = string
}

variable "dlq_arn" {
  description = "ARN of the stockyard-events-dlq queue."
  type        = string
}

variable "export_bucket_arn" {
  description = "ARN of the Parquet export bucket."
  type        = string
}

variable "export_prefix" {
  description = "Key prefix the exports are written under."
  type        = string
}

variable "signing_secret_arn" {
  description = "ARN of the Secrets Manager secret holding the webhook signing key."
  type        = string
}
