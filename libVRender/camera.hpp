void Camera::init(glm::vec3 stare, float dist, float width, float height, float minDist)
{
    this->stare = stare;
    this->distance = dist;
    this->_width = width;
    this->_height = height;
    this->_minDist = minDist;
    Reset(stare, dist);
    Resize(width, height);
}

void Camera::RotateAzimuth(float delta)
{
	Azimuth += delta * rotateSpeed;
	Azimuth = fmod(Azimuth, 2 * M_PI);
	// Apply azimuth range constraints
	Azimuth = glm::clamp(Azimuth, azimuth_range.x, azimuth_range.y);
	UpdatePosition();
}

void Camera::RotateAltitude(float delta)
{
	Altitude += delta * rotateSpeed;
	// Apply altitude range constraints
	Altitude = glm::clamp(Altitude, altitude_range.x, altitude_range.y);
	UpdatePosition();
}

void Camera::Rotate(float deltaAlt, float deltaAzi)
{
	Azimuth += deltaAzi * rotateSpeed;
	Azimuth = fmod(Azimuth, 2 * M_PI);
	// Apply azimuth range constraints
	Azimuth = glm::clamp(Azimuth, azimuth_range.x, azimuth_range.y);
	
	Altitude += deltaAlt * rotateSpeed;
	// Apply altitude range constraints
	Altitude = glm::clamp(Altitude, altitude_range.x, altitude_range.y);
	
	stare = position - glm::vec3(
		distance * cos(Altitude) * cos(Azimuth),
		distance * cos(Altitude) * sin(Azimuth),
		distance * sin(Altitude)
	);

	UpdatePosition();
}

void Camera::GoFrontBack(float delta)
{
	// Calculate the direction vector from position to stare
	glm::vec3 direction = glm::normalize(stare - position);
	
	// Move both stare and position along this direction
	glm::vec3 newStare = stare + direction * delta;
	glm::vec3 newPosition = position + direction * delta;
	
	// Apply xyz_range constraints if provided
	if (xyz_range.x != -FLT_MAX || xyz_range.y != FLT_MAX) {
		newStare.x = glm::clamp(newStare.x, xyz_range.x, xyz_range.y);
		newStare.y = glm::clamp(newStare.y, xyz_range.x, xyz_range.y);
		newStare.z = glm::clamp(newStare.z, xyz_range.x, xyz_range.y);
		
		newPosition.x = glm::clamp(newPosition.x, xyz_range.x, xyz_range.y);
		newPosition.y = glm::clamp(newPosition.y, xyz_range.x, xyz_range.y);
		newPosition.z = glm::clamp(newPosition.z, xyz_range.x, xyz_range.y);
	}
	
	stare = newStare;
	position = newPosition;
}

void Camera::PanLeftRight(float delta)
{
	glm::vec3 newStare = stare + moveRight * delta;
	glm::vec3 newPosition = position + moveRight * delta;
	
	// Apply xyz_range constraints if provided
	if (xyz_range.x != -FLT_MAX || xyz_range.y != FLT_MAX) {
		newStare.x = glm::clamp(newStare.x, xyz_range.x, xyz_range.y);
		newStare.y = glm::clamp(newStare.y, xyz_range.x, xyz_range.y);
		newStare.z = glm::clamp(newStare.z, xyz_range.x, xyz_range.y);
		
		newPosition.x = glm::clamp(newPosition.x, xyz_range.x, xyz_range.y);
		newPosition.y = glm::clamp(newPosition.y, xyz_range.x, xyz_range.y);
		newPosition.z = glm::clamp(newPosition.z, xyz_range.x, xyz_range.y);
	}
	
	stare = newStare;
	position = newPosition;
}

void Camera::PanBackForth(float delta)
{
	glm::vec3 newStare = stare + moveFront * delta;
	glm::vec3 newPosition = position + moveFront * delta;
	
	// Apply xyz_range constraints if provided
	if (xyz_range.x != -FLT_MAX || xyz_range.y != FLT_MAX) {
		newStare.x = glm::clamp(newStare.x, xyz_range.x, xyz_range.y);
		newStare.y = glm::clamp(newStare.y, xyz_range.x, xyz_range.y);
		newStare.z = glm::clamp(newStare.z, xyz_range.x, xyz_range.y);
		
		newPosition.x = glm::clamp(newPosition.x, xyz_range.x, xyz_range.y);
		newPosition.y = glm::clamp(newPosition.y, xyz_range.x, xyz_range.y);
		newPosition.z = glm::clamp(newPosition.z, xyz_range.x, xyz_range.y);
	}
	
	stare = newStare;
	position = newPosition;
}

void Camera::ElevateUpDown(float delta)
{
	glm::vec3 newStare = stare + glm::vec3(0.0f, 0.0f, 1.0f) * delta;
	glm::vec3 newPosition = position + glm::vec3(0.0f, 0.0f, 1.0f) * delta;
	
	// Apply xyz_range constraints if provided
	if (xyz_range.x != -FLT_MAX || xyz_range.y != FLT_MAX) {
		newStare.x = glm::clamp(newStare.x, xyz_range.x, xyz_range.y);
		newStare.y = glm::clamp(newStare.y, xyz_range.x, xyz_range.y);
		newStare.z = glm::clamp(newStare.z, xyz_range.x, xyz_range.y);
		
		newPosition.x = glm::clamp(newPosition.x, xyz_range.x, xyz_range.y);
		newPosition.y = glm::clamp(newPosition.y, xyz_range.x, xyz_range.y);
		newPosition.z = glm::clamp(newPosition.z, xyz_range.x, xyz_range.y);
	}
	
	stare = newStare;
	position = newPosition;
}

void Camera::Zoom(float delta)
{
	distance = glm::clamp(distance * (1 + delta), _minDist, 100.0f);
	UpdatePosition();
}

void Camera::Reset(glm::vec3 stare, float dist)
{
	this->stare = glm::vec3(stare.x, stare.y, 0.0f);
	distance = dist;
	Azimuth = -M_PI_2;
	Altitude = M_PI_2;
	UpdatePosition();
}

void Camera::Resize(float width, float height)
{
	_width = width;
	_height = height;
	_aspectRatio = _width / _height;
}


bool Camera::test_apply_external()
{
	return camera_object->anchor.obj != nullptr || glm::distance(camera_object->current_pos, camera_object->target_position) > 0.01f;
}

glm::mat4 Camera::GetViewMatrix()
{
	auto st = stare;
	auto pos = position;
	if (test_apply_external()) {
		st += camera_object->current_pos;
		pos += camera_object->current_pos;
	}
	auto mat = glm::lookAt(pos, st, up);
	if (isnan(mat[0][0])) throw "WTF?";
	return mat;
	// todo: try better view point
	// if (abs(position.z-stare.z)>distance/2)
	// return glm::lookAt(position, stare, up);
	// else
	// 	return lookAt(position, glm::vec3(stare.x,stare.y,))
}

glm::mat4 Camera::GetProjectionMatrix()
{
	if (ProjectionMode == 0) {
		return glm::perspective(glm::radians(_fov), _aspectRatio, cam_near, cam_far);
	}
	return glm::ortho(-_width * distance / OrthoFactor, _width * distance / OrthoFactor, -_height * distance / OrthoFactor, _height * distance / OrthoFactor, 1.0f, 100000.0f);
}

void Camera::UpdatePosition()
{
	if (abs(Altitude - M_PI_2) < gap || abs(Altitude + M_PI_2)<gap) {
		position = stare + glm::vec3(0.0f, 0.0f, (Altitude > 0 ? distance : -distance));
		glm::vec3 n = glm::vec3(cos(Azimuth), sin(Azimuth), 0.0f);
		moveRight = glm::normalize(glm::cross(glm::vec3(0.0f, 0.0f, 1.0f), n));
		up = moveFront = Altitude > 0 ? -n : n;
	}
	else {
		position = stare + glm::vec3(
			distance * cos(Altitude) * cos(Azimuth),
			distance * cos(Altitude) * sin(Azimuth),
			distance * sin(Altitude)
		);
		up = glm::vec3(0.0f, 0.0f, 1.0f);
		moveFront = -glm::vec3(cos(Azimuth), sin(Azimuth), 0.0f);
		if (Altitude < 0) moveFront *= -1;
		moveRight = glm::normalize(glm::cross(up, position - stare));
	}
}
 