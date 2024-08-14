variable "name" {
  description = "Cluster name, e.g. aks-parcel-trace-dev."
  type        = string
}

variable "location" {
  type = string
}

variable "resource_group_name" {
  type = string
}

variable "dns_prefix" {
  type = string
}

variable "kubernetes_version" {
  description = "AKS minor version; null lets AKS pick the default."
  type        = string
  default     = "1.29"
}

variable "sku_tier" {
  description = "Free for dev, Standard (uptime SLA) for prod."
  type        = string
  default     = "Free"
}

variable "vnet_cidr" {
  type    = string
  default = "10.20.0.0/16"
}

variable "node_subnet_cidr" {
  type    = string
  default = "10.20.0.0/20"
}

variable "service_cidr" {
  type    = string
  default = "10.21.0.0/16"
}

variable "dns_service_ip" {
  type    = string
  default = "10.21.0.10"
}

variable "zones" {
  type    = list(string)
  default = null
}

variable "system_node_vm_size" {
  type    = string
  default = "Standard_D2s_v5"
}

variable "system_node_min_count" {
  type    = number
  default = 1
}

variable "system_node_max_count" {
  type    = number
  default = 2
}

variable "user_node_vm_size" {
  type    = string
  default = "Standard_D4s_v5"
}

variable "user_node_min_count" {
  type    = number
  default = 1
}

variable "user_node_max_count" {
  type    = number
  default = 3
}

variable "log_analytics_workspace_id" {
  description = "Workspace for Container Insights."
  type        = string
}

variable "acr_id" {
  description = "Registry the kubelet identity gets AcrPull on."
  type        = string
}

variable "tags" {
  type    = map(string)
  default = {}
}
