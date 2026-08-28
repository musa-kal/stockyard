variable "name" {
  description = "Prefix for alarm and topic names."
  type        = string
}

variable "alarm_email" {
  description = "Address subscribed to the alarm topic. Null leaves the topic without subscribers."
  type        = string
  default     = null
}
