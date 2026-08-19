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
