variable "subscription_id" {
  type      = string
  sensitive = true
}

variable "location" {
  type    = string
  default = "eastus"

}

resource "time_static" "created" {}

locals {
  tags = {
    cost-center = "rkw"
    environment = "dev"
    team        = "briane"
    created-by  = "terraform"
    created-on  = time_static.created.rfc3339
    # updated-on  = timestamp()
  }
}