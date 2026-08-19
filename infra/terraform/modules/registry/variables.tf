variable "repositories" {
  description = "ECR repository names, one per deployable image."
  type        = set(string)
}

variable "force_delete" {
  description = "Let terraform destroy delete repositories that still hold images."
  type        = bool
  default     = false
}
