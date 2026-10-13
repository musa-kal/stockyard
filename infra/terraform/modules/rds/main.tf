resource "aws_db_subnet_group" "this" {
  name       = var.name
  subnet_ids = var.subnet_ids
}

resource "aws_security_group" "this" {
  name        = "${var.name}-sql"
  description = "SQL Server access for Stockyard workloads"
  vpc_id      = var.vpc_id
}

resource "aws_vpc_security_group_ingress_rule" "sql" {
  for_each = var.allowed_security_group_ids

  security_group_id            = aws_security_group.this.id
  description                  = "SQL Server from the EKS nodes"
  ip_protocol                  = "tcp"
  from_port                    = 1433
  to_port                      = 1433
  referenced_security_group_id = each.value
}

resource "aws_db_instance" "this" {
  identifier     = "${var.name}-sql"
  engine         = "sqlserver-ex"
  engine_version = var.engine_version
  license_model  = "license-included"
  instance_class = var.instance_class

  allocated_storage     = var.allocated_storage
  max_allocated_storage = var.max_allocated_storage
  storage_type          = "gp3"
  storage_encrypted     = true

  # The master password is generated and stored by RDS in Secrets Manager, so
  # it never appears in Terraform state or in a variable.
  username                    = "stockyard_admin"
  manage_master_user_password = true

  db_subnet_group_name   = aws_db_subnet_group.this.name
  vpc_security_group_ids = [aws_security_group.this.id]
  publicly_accessible    = false
  multi_az               = false

  backup_retention_period    = var.backup_retention_days
  backup_window              = "06:00-07:00"
  maintenance_window         = "sun:07:30-sun:08:30"
  auto_minor_version_upgrade = true
  copy_tags_to_snapshot      = true

  deletion_protection       = var.deletion_protection
  skip_final_snapshot       = false
  final_snapshot_identifier = "${var.name}-sql-final"
}
