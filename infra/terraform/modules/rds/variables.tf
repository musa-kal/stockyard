variable "name" {
  description = "Prefix for the instance identifier and related resources."
  type        = string
}

variable "vpc_id" {
  description = "VPC the instance lives in."
  type        = string
}

variable "subnet_ids" {
  description = "Private subnets for the DB subnet group."
  type        = list(string)
}

variable "allowed_security_group_ids" {
  description = "Security groups allowed to connect on 1433, keyed by a static label (the IDs are unknown until apply), normally the EKS cluster security group the nodes use."
  type        = map(string)
}

variable "instance_class" {
  description = "RDS instance class. SQL Server Express runs on the db.t3 family."
  type        = string
}

variable "engine_version" {
  description = "SQL Server engine version prefix. 16.00 is SQL Server 2022; minor versions follow auto upgrades."
  type        = string
}

variable "allocated_storage" {
  description = "Initial gp3 storage in GiB. 20 is the RDS minimum for SQL Server."
  type        = number
}

variable "max_allocated_storage" {
  description = "Ceiling for storage autoscaling in GiB."
  type        = number
}

variable "backup_retention_days" {
  description = "Days of automated backups kept."
  type        = number
}

variable "deletion_protection" {
  description = "Block deletion of the instance until this is turned off."
  type        = bool
}
