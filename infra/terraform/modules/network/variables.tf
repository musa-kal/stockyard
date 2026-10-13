variable "name" {
  description = "Prefix for resource names."
  type        = string
}

variable "cidr_block" {
  description = "CIDR block for the VPC. Subnets are carved out of it as /20s."
  type        = string
}

variable "availability_zones" {
  description = "Availability zones to spread subnets across, one public and one private subnet each."
  type        = list(string)
}

variable "cluster_name" {
  description = "EKS cluster name, used for the subnet discovery tags load balancers rely on."
  type        = string
}
