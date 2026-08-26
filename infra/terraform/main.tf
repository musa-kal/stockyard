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

  bucket_name    = "${var.project}-exports-${data.aws_caller_identity.current.account_id}"
  retention_days = var.export_retention_days
}

module "registry" {
  source = "./modules/registry"

  repositories = [
    "stockyard-api",
    "stockyard-projections",
    "stockyard-notifier",
    "stockyard-exporter",
  ]
}

module "eks" {
  source = "./modules/eks"

  cluster_name                 = local.cluster_name
  cluster_version              = var.cluster_version
  subnet_ids                   = module.network.private_subnet_ids
  endpoint_public_access_cidrs = var.cluster_endpoint_public_access_cidrs

  node_instance_types = var.node_instance_types
  node_min_size       = var.node_min_size
  node_desired_size   = var.node_desired_size
  node_max_size       = var.node_max_size

  queue_arn         = module.messaging.queue_arn
  dlq_arn           = module.messaging.dlq_arn
  export_bucket_arn = module.storage.bucket_arn
  export_prefix     = module.storage.export_prefix

  signing_secret_arn = aws_secretsmanager_secret.signing.arn
}

module "rds" {
  source = "./modules/rds"

  name                       = local.name
  vpc_id                     = module.network.vpc_id
  subnet_ids                 = module.network.private_subnet_ids
  allowed_security_group_ids = [module.eks.cluster_security_group_id]
  instance_class             = var.db_instance_class
  engine_version             = var.db_engine_version
  allocated_storage          = var.db_allocated_storage
  max_allocated_storage      = var.db_max_allocated_storage
  backup_retention_days      = var.db_backup_retention_days
  deletion_protection        = var.db_deletion_protection
}
