variable "bucket_name" {
  description = "Globally unique name of the export bucket."
  type        = string
}

variable "export_prefix" {
  description = "Key prefix the Exporter writes under (Exporter__Destination is s3://<bucket>/<prefix>)."
  type        = string
  default     = "exports"
}

variable "force_destroy" {
  description = "Let terraform destroy delete a bucket that still holds exports."
  type        = bool
  default     = false
}
