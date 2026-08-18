variable "aws_region" {
  description = "Region every resource is created in. The application and the LocalStack setup both assume us-east-1."
  type        = string
  default     = "us-east-1"
}

variable "project" {
  description = "Short name used as the prefix for resource names and the Project tag."
  type        = string
  default     = "stockyard"
}

variable "environment" {
  description = "Environment name, used in tags and in a few names that must be unique per account."
  type        = string
  default     = "dev"
}

variable "tags" {
  description = "Extra tags merged into the provider default tags."
  type        = map(string)
  default     = {}
}

variable "vpc_cidr" {
  description = "CIDR block for the VPC. A /16 leaves room for /20 subnets in up to eight zones."
  type        = string
  default     = "10.40.0.0/16"
}

variable "availability_zone_count" {
  description = "How many availability zones to spread subnets over. EKS needs at least two."
  type        = number
  default     = 2
}

variable "export_retention_days" {
  description = "Days the Parquet exports are kept in S3 before they expire. dbt only needs the days it has not loaded yet."
  type        = number
  default     = 90

  validation {
    condition     = var.export_retention_days >= 7
    error_message = "Keep at least a week of exports so a failed dbt run can catch up."
  }
}
