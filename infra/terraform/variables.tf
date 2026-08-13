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
