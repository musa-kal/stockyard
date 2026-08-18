data "aws_availability_zones" "available" {
  state = "available"

  filter {
    name   = "opt-in-status"
    values = ["opt-in-not-required"]
  }
}

data "aws_caller_identity" "current" {}

locals {
  name         = "${var.project}-${var.environment}"
  cluster_name = local.name
  azs          = slice(data.aws_availability_zones.available.names, 0, var.availability_zone_count)
}

module "network" {
  source = "./modules/network"

  name               = local.name
  cidr_block         = var.vpc_cidr
  availability_zones = local.azs
  cluster_name       = local.cluster_name
}

module "messaging" {
  source = "./modules/messaging"
}

module "storage" {
  source = "./modules/storage"

  bucket_name = "${var.project}-exports-${data.aws_caller_identity.current.account_id}"
}
