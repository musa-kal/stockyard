variable "repositories" {
  description = "ECR repository names, one per deployable image."
  type        = set(string)
}

variable "force_delete" {
  description = "Let terraform destroy delete repositories that still hold images."
  type        = bool
  default     = false
}

variable "keep_images" {
  description = "Tagged images kept per repository; older ones are expired by the lifecycle policy."
  type        = number
  default     = 20
}
