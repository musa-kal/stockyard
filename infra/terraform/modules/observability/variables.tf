variable "name" {
  description = "Prefix for alarm and topic names."
  type        = string
}

variable "alarm_email" {
  description = "Address subscribed to the alarm topic. Null leaves the topic without subscribers."
  type        = string
  default     = null
}

variable "queue_name" {
  description = "Name of the events queue."
  type        = string
}

variable "dlq_name" {
  description = "Name of the dead-letter queue."
  type        = string
}

variable "max_message_age_seconds" {
  description = "Age of the oldest message on the events queue that counts as the projections worker falling behind."
  type        = number
  default     = 300
}
