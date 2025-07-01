variable "subscription_id" {
  type      = string
  sensitive = true
}

variable "location" {
  type    = string
  default = "eastus"

}

locals {
  tags = {
    cost-center = "rkw"
    environment = "dev"
    team        = "team-ba"
  }
}
