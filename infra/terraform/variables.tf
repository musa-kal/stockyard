variable "aws_region" {
  description = "Region every resource is created in. The application and the LocalStack setup both assume us-east-1."
  type        = string
  default     = "us-east-1"

  validation {
    condition     = can(regex("^[a-z]{2}(-gov)?-[a-z]+-[0-9]$", var.aws_region))
    error_message = "aws_region must look like a region code, for example us-east-1."
  }
}

variable "project" {
  description = "Short name used as the prefix for resource names and the Project tag."
  type        = string
  default     = "stockyard"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{2,19}$", var.project))
    error_message = "project must be 3 to 20 lowercase letters, digits, or hyphens, starting with a letter. It ends up in bucket and role names."
  }
}

variable "environment" {
  description = "Environment name, used in tags and in a few names that must be unique per account."
  type        = string
  default     = "dev"

  validation {
    condition     = can(regex("^[a-z0-9]{2,10}$", var.environment))
    error_message = "environment must be 2 to 10 lowercase letters or digits."
  }
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

  validation {
    condition     = can(cidrhost(var.vpc_cidr, 0)) && tonumber(split("/", var.vpc_cidr)[1]) <= 20
    error_message = "vpc_cidr must be a valid IPv4 CIDR no smaller than /20, since subnets are carved out as four bits longer."
  }
}

variable "availability_zone_count" {
  description = "How many availability zones to spread subnets over. EKS needs at least two."
  type        = number
  default     = 2

  validation {
    condition     = var.availability_zone_count >= 2 && var.availability_zone_count <= 3
    error_message = "EKS needs subnets in at least two zones; more than three only adds cost here."
  }
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

variable "cluster_version" {
  description = "Kubernetes minor version for EKS, for example 1.34. Upgrades go one minor version at a time."
  type        = string
  default     = "1.34"

  validation {
    condition     = can(regex("^1[.][0-9]{2}$", var.cluster_version))
    error_message = "cluster_version is a Kubernetes minor version such as 1.34, without a patch number."
  }
}

variable "cluster_endpoint_public_access_cidrs" {
  description = "CIDR blocks allowed to call the public EKS API endpoint. Narrow this to your own address and the pipeline agents where you can."
  type        = list(string)
  default     = ["0.0.0.0/0"]
}

variable "node_instance_types" {
  description = "Instance types for the managed node group. Two t3.medium nodes fit the four services, the migrate job, and the system add-ons."
  type        = list(string)
  default     = ["t3.medium"]

  validation {
    condition     = length(var.node_instance_types) > 0
    error_message = "Give the node group at least one instance type."
  }
}

variable "node_min_size" {
  description = "Minimum number of worker nodes."
  type        = number
  default     = 2
}

variable "node_desired_size" {
  description = "Worker nodes at creation time."
  type        = number
  default     = 2

  validation {
    condition     = var.node_desired_size >= var.node_min_size && var.node_desired_size <= var.node_max_size
    error_message = "node_desired_size must sit between node_min_size and node_max_size."
  }
}

variable "node_max_size" {
  description = "Maximum number of worker nodes."
  type        = number
  default     = 4
}

variable "db_instance_class" {
  description = "RDS instance class for SQL Server Express. db.t3.small (2 GiB) is the smallest that holds the working set comfortably."
  type        = string
  default     = "db.t3.small"

  validation {
    condition     = startswith(var.db_instance_class, "db.")
    error_message = "db_instance_class is an RDS class name and starts with db., for example db.t3.small."
  }
}

variable "db_engine_version" {
  description = "SQL Server engine version prefix. 16.00 selects SQL Server 2022."
  type        = string
  default     = "16.00"
}

variable "db_allocated_storage" {
  description = "Initial gp3 storage in GiB."
  type        = number
  default     = 20

  validation {
    condition     = var.db_allocated_storage >= 20
    error_message = "RDS for SQL Server needs at least 20 GiB."
  }
}

variable "db_max_allocated_storage" {
  description = "Upper bound for RDS storage autoscaling in GiB. Express caps each database at 10 GB regardless."
  type        = number
  default     = 50

  validation {
    condition     = var.db_max_allocated_storage >= var.db_allocated_storage
    error_message = "db_max_allocated_storage must be at least db_allocated_storage."
  }
}

variable "db_backup_retention_days" {
  description = "Days of automated backups (point in time restore window)."
  type        = number
  default     = 7

  validation {
    condition     = var.db_backup_retention_days >= 1 && var.db_backup_retention_days <= 35
    error_message = "Keep between 1 and 35 days of backups; 0 would turn off point in time restore."
  }
}

variable "db_deletion_protection" {
  description = "Protect the instance from deletion. Turn off deliberately before a teardown."
  type        = bool
  default     = true
}

variable "secret_recovery_window_days" {
  description = "Days a deleted secret can still be restored. 0 deletes immediately, which is only sensible in a throwaway account."
  type        = number
  default     = 7

  validation {
    condition     = var.secret_recovery_window_days == 0 || (var.secret_recovery_window_days >= 7 && var.secret_recovery_window_days <= 30)
    error_message = "Secrets Manager accepts 0 or a value from 7 to 30."
  }
}

variable "cluster_admin_principal_arns" {
  description = "IAM principal ARNs given cluster admin through EKS access entries: the role the pipeline's AWS service connection assumes, and your own role for kubectl."
  type        = list(string)
  default     = []
}

variable "log_retention_days" {
  description = "Retention for CloudWatch log groups (EKS control plane and container logs)."
  type        = number
  default     = 30

  validation {
    condition     = contains([1, 3, 5, 7, 14, 30, 60, 90, 120, 150, 180, 365], var.log_retention_days)
    error_message = "Use one of the retention values CloudWatch Logs accepts up to a year: 1, 3, 5, 7, 14, 30, 60, 90, 120, 150, 180, 365."
  }
}

variable "alarm_email" {
  description = "Email address that receives CloudWatch alarm notifications. Leave null to skip the subscription."
  type        = string
  default     = null
}
