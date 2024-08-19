variable "subscription_id" {
  description = "Target subscription (placeholder in the example tfvars)."
  type        = string
}

variable "environment" {
  type    = string
  default = "dev"
}

variable "location" {
  type    = string
  default = "westeurope"
}

variable "kubernetes_version" {
  type    = string
  default = "1.29"
}

variable "cost_center" {
  type    = string
  default = "logistics-platform"
}

variable "extra_tags" {
  type    = map(string)
  default = {}
}

variable "mongo_connection_string" {
  description = "MongoDB for dispatch (dev runs it in-cluster; this is only stored in Key Vault)."
  type        = string
  sensitive   = true
}

variable "redis_connection_string" {
  description = "Redis for the tracking service's latest positions."
  type        = string
  sensitive   = true
}
