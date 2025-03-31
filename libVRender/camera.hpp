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
	UpdatePosition();
}

void Camera::RotateAltitude(float delta)
{
	Altitude += delta * rotateSpeed;
	Altitude = glm::clamp(Altitude, float(-M_PI_2), float(M_PI_2));
	UpdatePosition();
}

void Camera::Rotate(float deltaAlt, float deltaAzi)
{
	Azimuth += deltaAzi * rotateSpeed;
	Azimuth = fmod(Azimuth, 2 * M_PI);
	Altitude += deltaAlt * rotateSpeed;
	Altitude = glm::clamp(Altitude, float(-M_PI_2), float(M_PI_2));
	stare = position - glm::vec3(
		distance * cos(Altitude) * cos(Azimuth),
		distance * cos(Altitude) * sin(Azimuth),
		distance * sin(Altitude)
	);

	UpdatePosition();
}

void Camera::PanLeftRight(float delta)
{
	stare += moveRight * delta;
	position += moveRight * delta;
}

void Camera::PanBackForth(float delta)
{
	stare += moveFront * delta;
	position += moveFront * delta;
}

void Camera::ElevateUpDown(float delta)
{
	stare += glm::vec3(0.0f, 0.0f, 1.0f) * delta;
	position += glm::vec3(0.0f, 0.0f, 1.0f) * delta;
}

void Camera::Zoom(float delta)
{
	distance = glm::clamp(distance * (1 + delta), _minDist, std::numeric_limits<float>::max());
	if (distance > 100) 
		distance = 100; //threshing distance.
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

glm::mat4 Camera::GetViewMatrix()
{
	auto mat = glm::lookAt(position, stare, up);
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
 